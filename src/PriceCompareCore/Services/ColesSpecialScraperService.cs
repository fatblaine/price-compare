using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using PriceCompareCore.Interfaces;
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

        public ColesSpecialScraperService(HttpClient httpClient, ILogger<ColesSpecialScraperService> logger, IDistributedCache cache)
        {
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders
                .Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _logger = logger;
            _cache = cache;

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
                        ScrapedAt = DateTime.UtcNow
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

        private List<ColesSpecialProduct> ApplyFilters(List<ColesSpecialProduct> allProducts, ColesSpecialProductRequest request)
        {
            throw new NotImplementedException();
        }

        private async Task<HtmlDocument> LoadHtmlDocumentAsync(string url)
        {
            throw new NotImplementedException();
        }

        private List<ColesSpecialProduct> ParseProductsFromHtml(HtmlDocument htmlDocument)
        {
            throw new NotImplementedException();
        }
    }
}