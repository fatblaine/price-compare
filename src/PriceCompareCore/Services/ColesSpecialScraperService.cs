using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using PriceCompareCore.Interfaces;
using PriceCompareData.Common;
using PriceCompareData.Data;
using PriceCompareData.DTOs;
using PriceCompareData.Entities;
using PriceCompareData.Entities.Common;
using PriceCompareData.Entities.History;

namespace PriceCompareCore.Services
{
    public class ColesSpecialScraperService : IColesSpecialScraperService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ColesSpecialScraperService> _logger;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly IDistributedCache _cache;
        private const string BaseUrl = WebInfo.COLES_BASE_URL;

        private readonly AppDbContext _dbContext;

        public ColesSpecialScraperService(HttpClient httpClient, ILogger<ColesSpecialScraperService> logger, IDistributedCache cache, AppDbContext dbContext)
        {
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders
                .Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _logger = logger;
            _cache = cache;
            _dbContext = dbContext;

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

        public async Task<List<ColesSpecialProduct>> GetAllOnSpecialProductsAsync(ColesSpecialProductRequest request)
        {
            List<ColesSpecialProduct> allProducts = new List<ColesSpecialProduct>();

            var cachedData = await _cache.GetStringAsync("ColesSpecialProducts");
            if (!string.IsNullOrEmpty(cachedData))
            {
                _logger.LogInformation("Fetching Coles special products from cache.");
                allProducts = JsonSerializer.Deserialize<List<ColesSpecialProduct>>(cachedData);
            }
            else
            {
                allProducts = new List<ColesSpecialProduct>();
                int page = 1;
                bool hasMorePages = true;

                while (hasMorePages)
                {
                    string url = page == 1 ? $"{BaseUrl}/on-special" : $"{BaseUrl}/on-special?page={page}";

                    try
                    {
                        var htmlDocument = await _retryPolicy.ExecuteAsync(async () => await LoadHtmlDocumentAsync(url));

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
                    CacheKey.COLES_ON_SPECIAL_PRODUCTS,
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
                .Any(p => p.Name == product.Name && p.ScrapedAt.Date == today);

                if (!alreadyExists)
                {
                    _dbContext.PriceHistory.Add(new PriceHistory
                    {
                        Name = product.Name,
                        ImageUrl = product.ImageUrl,
                        CurrentPrice = product.CurrentPrice,
                        ScrapedAt = DateTime.UtcNow,
                        OfferType = OfferType.ON_SPECIAL,
                        ShopType = ShopType.COLES
                    });
                }
            }
            await _dbContext.SaveChangesAsync();

            // filters
            if (request != null)
            {
                allProducts = ApplyFilters(allProducts, request);
            }

            return allProducts;
        }

        private List<ColesSpecialProduct> ApplyFilters(List<ColesSpecialProduct> products, ColesSpecialProductRequest request)
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

        private List<ColesSpecialProduct> ParseProductsFromHtml(HtmlDocument htmlDocument)
        {
            var products = new List<ColesSpecialProduct>();
            var productNodes = htmlDocument.DocumentNode.SelectNodes(Xpath.SPECIAL_PRODUCTS_NODE);
            if (productNodes == null)
            {
                _logger.LogInformation("No product nodes found in the HTML document.");
                return products;
            }

            foreach (var productNode in productNodes)
            {
                try
                {
                    var product = new ColesSpecialProduct();
                    // get product name
                    var nameNode = productNode.SelectSingleNode(Xpath.SPECIAL_PRODUCTS_NAME);
                    if (nameNode != null)
                    {
                        product.Name = WebUtility.HtmlDecode(nameNode.InnerText.Trim());
                    }
                    // get product price
                    var priceNode = productNode.SelectSingleNode(Xpath.SPECIAL_PRODUCTS_PRICE);
                    if (priceNode != null)
                    {
                        var priceText = priceNode.InnerText.Trim().Replace("$", "");
                        if (decimal.TryParse(priceText, out decimal price))
                        {
                            product.CurrentPrice = price;
                        }
                    }
                    // get price per unit
                    var pricePerUnitNode = productNode.SelectSingleNode(Xpath.SPECIAL_PRODUCTS_PRICE_PER_UNIT);
                    if (pricePerUnitNode != null)
                    {
                        // remove "Was $" part
                        var text = pricePerUnitNode.InnerText.Trim();
                        // If it contains "Was $", only take the first half
                        var unitPriceText = text.Split("Was")[0].Trim();
                        product.PricePerUnit = unitPriceText;
                    }
                    // get original price info
                    var wasPriceNode = productNode.SelectSingleNode(Xpath.SPECIAL_PRODUCTS_ORIGINAL_PRICE);
                    if (wasPriceNode != null)
                    {
                        product.WasPriceText = wasPriceNode.InnerText.Trim();

                        var match = Regex.Match(product.WasPriceText, @"Was \$(\d+(\.\d{1,2})?)");
                        if (match.Success && decimal.TryParse(match.Groups[1].Value, out decimal originalPrice))
                        {
                            product.OriginalPrice = originalPrice;
                        }
                    }
                    // get product image URL
                    var imageNode = productNode.SelectSingleNode(Xpath.SPECIAL_PRODUCTS_IMAGE_URL);
                    if (imageNode != null && imageNode.Attributes["src"] != null)
                    {
                        product.ImageUrl = imageNode.Attributes["src"].Value;
                    }
                    // check if product is sponsored
                    var sponsoredNode = productNode.SelectSingleNode(Xpath.SPECIAL_PRODUCTS_IS_SPONSORED);
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