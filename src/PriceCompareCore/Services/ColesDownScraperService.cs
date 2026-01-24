using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using PriceCompareCore.Interfaces;
using PriceCompareData.Entities;
using PriceCompareData.Entities.Common;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using PriceCompareData.DTOs;
using PriceCompareData.Common;
using PriceCompareData.Data;
using PriceCompareData.Entities.History;
using Microsoft.EntityFrameworkCore;



namespace PriceCompareCore.Services
{
    public class ColesDownScraperService : IColesDownScraperService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ColesDownScraperService> _logger;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly IDistributedCache _cache;
        private const string BaseUrl = WebInfo.COLES_BASE_URL;
        private readonly IIngestionService _ingestion;

        private readonly AppDbContext _dbContext;

        public ColesDownScraperService(AppDbContext db, ILogger<ColesDownScraperService> logger)
        {
            _dbContext = db;
            _logger = logger;
        }

        public ColesDownScraperService(HttpClient httpClient,
        ILogger<ColesDownScraperService> logger, IDistributedCache cache, AppDbContext dbContext, IIngestionService ingestion)
        {
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders
                .Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _logger = logger;
            _cache = cache;
            _dbContext = dbContext;
            _ingestion = ingestion;

            // polly retry policy
            _retryPolicy = Policy
                .Handle<HttpRequestException>()
                .Or<WebException>()
                .WaitAndRetryAsync(3, retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        Console.WriteLine($"Retry {retryCount} after {timeSpan.TotalSeconds}s due to: {exception.Message}");
                    });
        }

        // get all down down products
        public async Task<List<ColesDownProduct>> GetAllDownDownProductsAsync(ColesDownProductRequest request = null)
        {
            List<ColesDownProduct> allProducts;

            var cachedData = await _cache.GetStringAsync(CacheKey.COLES_DOWNDOWN_PRODUCTS);
            if (!string.IsNullOrEmpty(cachedData))
            {
                _logger.LogInformation("Returning cached Down Down products from Redis.");
                allProducts = JsonSerializer.Deserialize<List<ColesDownProduct>>(cachedData);
            }
            else
            {
                allProducts = new List<ColesDownProduct>();
                int page = 1;
                bool hasMorePages = true;

                while (hasMorePages)
                {
                    string url = page == 1 ?
                        $"{BaseUrl}/browse/down-down" :
                        $"{BaseUrl}/browse/down-down?page={page}";

                    try
                    {
                        // send request by using Polly
                        var htmlDocument = await _retryPolicy.ExecuteAsync(async () =>
                            await LoadHtmlDocumentAsync(url));

                        if (htmlDocument == null)
                        {
                            _logger.LogWarning($"Failed to load HTML document from: {url}");
                            hasMorePages = false;
                            continue;
                        }

                        // get products
                        var products = ParseProductsFromHtml(htmlDocument);
                        if (products.Count > 0)
                        {
                            allProducts.AddRange(products);
                            page++;
                            _logger.LogInformation($"Scraped data of {page - 1} pages, there are {products.Count} products in total.");
                        }
                        else
                        {
                            hasMorePages = false;
                            _logger.LogInformation($"No products found on page {page}, stopping pagination.");
                        }

                        // Delay to avoid overwhelming the server
                        await Task.Delay(TimeSpan.FromSeconds(1));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error scraping page {page}: {ex.Message}");
                        hasMorePages = false;
                    }
                }

                // redis
                var serializedData = JsonSerializer.Serialize(allProducts);
                await _cache.SetStringAsync(
                    CacheKey.COLES_DOWNDOWN_PRODUCTS,
                    serializedData,
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60)
                    });
            }

            // add price histories to database
            foreach (var product in allProducts)
            {
                var today = DateTime.UtcNow.Date;
                bool alreadyExists = _dbContext.PriceHistory
                .Any(p => p.Name == product.Name && p.ScrapedAt >= today && p.ScrapedAt < today.AddDays(1));

                if (!alreadyExists)
                {
                    _dbContext.PriceHistory.Add(new PriceHistory
                    {
                        Id = product.Id,
                        Name = product.Name,
                        ImageUrl = product.ImageUrl,
                        CurrentPrice = product.CurrentPrice,
                        ScrapedAt = DateTime.UtcNow,
                        OfferType = OfferType.DOWN_DOWN,
                        ShopType = ShopType.COLES
                    });
                }
            }
            await _dbContext.SaveChangesAsync();
            await _ingestion.UpsertColesDownAsync(allProducts);

            // filters
            if (request != null)
            {
                allProducts = ApplyFilters(allProducts, request);
            }

            return allProducts;
        }

        public async Task<List<PriceHistory>> GetPriceHistoryAsync(string name, int offerType, int shopType)
        {
            var since = DateTime.UtcNow.AddMonths(-1);
            return await _dbContext.PriceHistory
                .Where(p => p.Name == name && p.ScrapedAt >= since && p.OfferType == offerType && p.ShopType == shopType)
                .OrderBy(p => p.ScrapedAt)
                .ToListAsync();
        }

        public async Task<int> CleanOldPriceHistoryAsync()
        {
            var now = DateTime.UtcNow;
            var threshold = new DateTime(now.Year, now.Month, 1).AddMonths(-2);
            return await _dbContext.PriceHistory
                .Where(p => p.ScrapedAt < threshold)
                .ExecuteDeleteAsync();
        }





        // multi conditions filter
        private List<ColesDownProduct> ApplyFilters(List<ColesDownProduct> products, ColesDownProductRequest request)
        {
            var query = products.AsQueryable();
            // product name
            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                query = query.Where(p => !string.IsNullOrEmpty(p.Name) && p.Name.Contains(request.Name, StringComparison.OrdinalIgnoreCase));
            }
            // isSponsored
            if (request.IsSponsored)
            {
                query = query.Where(p => p.IsSponsored);
            }
            // current price
            if (request.MinPrice.HasValue)
            {
                query = query.Where(p => p.CurrentPrice >= request.MinPrice.Value);
            }
            if (request.MaxPrice.HasValue)
            {
                query = query.Where(p => p.CurrentPrice <= request.MaxPrice.Value);
            }

            return query.ToList();
        }

        // load html using html agility pack
        private async Task<HtmlDocument> LoadHtmlDocumentAsync(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var htmlContent = await response.Content.ReadAsStringAsync();
                var htmlDocument = new HtmlDocument();
                htmlDocument.LoadHtml(htmlContent);

                return htmlDocument;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to load HTML document from: {url}");
                return null;
            }
        }

        // Parse product information from HTML
        private List<ColesDownProduct> ParseProductsFromHtml(HtmlDocument htmlDocument)
        {
            var products = new List<ColesDownProduct>();
            var productNodes = htmlDocument.DocumentNode.SelectNodes(Xpath.DOWN_DOWN_PRODUCTS_NODE);
            if (productNodes == null)
            {
                _logger.LogInformation("No product nodes found in the HTML document.");
                return products;
            }

            foreach (var productNode in productNodes)
            {
                try
                {
                    var product = new ColesDownProduct();
                    // get product name
                    var nameNode = productNode.SelectSingleNode(Xpath.DOWN_DOWN_PRODUCTS_NAME);
                    if (nameNode != null)
                    {
                        product.Name = nameNode.InnerText.Trim();
                    }
                    // get product price
                    var priceNode = productNode.SelectSingleNode(Xpath.DOWN_DOWN_PRODUCTS_PRICE);
                    if (priceNode != null)
                    {
                        var priceText = priceNode.InnerText.Trim().Replace("$", "");
                        if (decimal.TryParse(priceText, out decimal price))
                        {
                            product.CurrentPrice = price;
                        }
                    }
                    // get price per unit
                    var pricePerUnitNode = productNode.SelectSingleNode(Xpath.DOWN_DOWN_PRODUCTS_PRICE_PER_UNIT);
                    if (pricePerUnitNode != null)
                    {
                        var pricePerUnitText = pricePerUnitNode.InnerText.Trim();
                        var match = System.Text.RegularExpressions.Regex.Match(pricePerUnitText, @"^\$(\d+(?:\.\d+)?)\s+per\s+([a-z0-9]+)");
                        product.PricePerUnit = match.Success ? match.Value : pricePerUnitText;
                    }
                    // get original price info
                    var wasPriceNode = productNode.SelectSingleNode(Xpath.DOWN_DOWN_PRODUCTS_ORIGINAL_PRICE);
                    if (wasPriceNode != null)
                    {
                        product.WasPriceText = wasPriceNode.InnerText.Trim();
                    }
                    // get value of original price
                    var wasPriceMatch = System.Text.RegularExpressions.Regex.Match(
                            product.WasPriceText,
                            @"Was \$(\d+\.\d{2})"
                        );
                    if (wasPriceMatch.Success && wasPriceMatch.Groups.Count > 1)
                    {
                        if (decimal.TryParse(wasPriceMatch.Groups[1].Value, out decimal originalPrice))
                        {
                            product.OriginalPrice = originalPrice;
                        }
                    }
                    // get product image URL
                    var imageNode = productNode.SelectSingleNode(Xpath.DOWN_DOWN_PRODUCTS_IMAGE_URL);
                    if (imageNode != null && imageNode.Attributes["src"] != null)
                    {
                        product.ImageUrl = imageNode.Attributes["src"].Value;
                    }
                    // check if product is sponsored
                    var sponsoredNode = productNode.SelectSingleNode(Xpath.DOWN_DOWN_PRODUCTS_IS_SPONSORED);
                    product.IsSponsored = sponsoredNode != null;

                    product.ScrapedAt = DateTime.UtcNow;

                    products.Add(product);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing product information: {ex.Message}");
                }
            }

            return products;
        }
    }
}
