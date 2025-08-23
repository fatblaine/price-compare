using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net; // Add this for WebException
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using PriceCompareCore.Interfaces;
using PriceCompareData.Entities;
using PriceCompareData.Entities.Common;

namespace PriceCompareCore.Services
{
    public class ColesScraperService : IScraperService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ColesScraperService> _logger;
        private readonly AsyncRetryPolicy _retryPolicy;
        private const string BaseUrl = WebInfo.COLES_BASE_URL;

        public ColesScraperService(HttpClient httpClient, ILogger<ColesScraperService> logger)
        {
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders
                .Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _logger = logger;

            // polly retry policy
            _retryPolicy = Policy
                .Handle<HttpRequestException>()
                .Or<WebException>()
                .WaitAndRetryAsync(3, retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        // You may want to inject ILogger and use it here instead of Console.WriteLine
                        Console.WriteLine($"Retry {retryCount} after {timeSpan.TotalSeconds}s due to: {exception.Message}");
                    });
        }

        // get all down down products
        public async Task<List<ScrapedProduct>> GetAllDownDownProductsAsync()
        {
            var allProducts = new List<ScrapedProduct>();
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
            return allProducts;
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

        // Parse product information from HTML
        private List<ScrapedProduct> ParseProductsFromHtml(HtmlDocument htmlDocument)
        {
            var products = new List<ScrapedProduct>();
            var productNodes = htmlDocument.DocumentNode.SelectNodes("//*[@id='coles-targeting-product-tiles']//section[@data-testid='product-tile']");
            if (productNodes == null)
            {
                _logger.LogInformation("No product nodes found in the HTML document.");
                return products;
            }

            foreach (var productNode in productNodes)
            {
                try
                {
                    var product = new ScrapedProduct();
                    // get product name
                    var nameNode = productNode.SelectSingleNode(".//a[contains(@class, 'product__link')]/h2[contains(@class, 'product__title')]");
                    if (nameNode != null)
                    {
                        product.Name = nameNode.InnerText.Trim();
                    }
                    // get product price
                    var priceNode = productNode.SelectSingleNode(".//span[contains(@class, 'price')]");
                    if (priceNode != null)
                    {
                        var priceText = priceNode.InnerText.Trim().Replace("$", "");
                        if (decimal.TryParse(priceText, out decimal price))
                        {
                            product.CurrentPrice = price;
                        }
                    }
                    // get price per unit
                    var pricePerUnitNode = productNode.SelectSingleNode(".//div[contains(@class, 'price__calculation_method')]");
                    if (pricePerUnitNode != null)
                    {
                        var pricePerUnitText = pricePerUnitNode.InnerText.Trim();
                        var match = System.Text.RegularExpressions.Regex.Match(pricePerUnitText, @"^\$[\d\.]+ per [^\s]+");
                        product.PricePerUnit = match.Success ? match.Value : pricePerUnitText;
                    }
                    // get original price info
                    var wasPriceNode = productNode.SelectSingleNode(".//div[contains(@class, 'price__was')]/strong");
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
                    var imageNode = productNode.SelectSingleNode(".//img[@data-testid='product-image']");
                    if (imageNode != null && imageNode.Attributes["src"] != null)
                    {
                        product.ImageUrl = imageNode.Attributes["src"].Value;
                    }
                    // get product detail URL
                    // var linkNode = productNode.SelectSingleNode("");
                    // if (linkNode != null && linkNode.Attributes["href"] != null)
                    // {
                    //     product.ProductUrl = BaseUrl + linkNode.Attributes["href"].Value;
                    // }
                    // check if product is sponsored
                    var sponsoredNode = productNode.SelectSingleNode(".//li[contains(@class, 'product__top_messaging__item') and contains(text(), 'Sponsored')]");
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