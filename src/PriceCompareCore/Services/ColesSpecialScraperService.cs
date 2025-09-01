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
        private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

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

        public async Task<List<ColesSpecialProduct>> GetAllOnSpecialProductsAsync(ColesSpecialProductRequest request = null)
        {
            List<ColesSpecialProduct> allProducts;

            var cachedData = await _cache.GetStringAsync(CacheKey.COLES_ON_SPECIAL_PRODUCTS);
            if (!string.IsNullOrEmpty(cachedData))
            {
                allProducts = JsonSerializer.Deserialize<List<ColesSpecialProduct>>(cachedData);
                _logger.LogInformation($"Using cached Coles specials data.");
            }
            else
            {
                allProducts = new List<ColesSpecialProduct>();
                string buildId = await GetBuildIdAsync();

                int page = 1;
                bool hasMorePages = true;

                while (hasMorePages)
                {
                    string url = $"https://www.coles.com.au/_next/data/{buildId}/en/on-special.json?page={page}";
                    _logger.LogInformation($"Fetching Coles specials page {page} with buildId={buildId}");

                    string response = await _retryPolicy.ExecuteAsync(() => _httpClient.GetStringAsync(url));
                    var data = JsonSerializer.Deserialize<ColesApiResponse>(response, _jsonOptions);

                    var products = data?.PageProps?.SearchResults?.Results;

                    if (products == null || products.Count == 0)
                    {
                        hasMorePages = false;
                    }
                    else
                    {
                        // mapping to ColesSpecialProduct
                        allProducts.AddRange(products
                        .Where(p => p._type == "PRODUCT")
                        .Select(p => new ColesSpecialProduct
                        {
                            Id = p.Id,
                            Name = p.Name,
                            CurrentPrice = p.Pricing?.Now ?? 0,
                            OriginalPrice = p.Pricing?.Was ?? 0,
                            PricePerUnit = p.Pricing?.Comparable,
                            ImageUrl = p.ImageUris?.FirstOrDefault()?.Uri,
                            IsSponsored = p._type != "PRODUCT",
                            ScrapedAt = DateTime.UtcNow
                        }));

                        _logger.LogInformation($"Scraped {products.Count} products from page {page}");
                        page++;
                    }
                }

                // Cache the results
                await _cache.SetStringAsync(
                    CacheKey.COLES_ON_SPECIAL_PRODUCTS,
                    JsonSerializer.Serialize(allProducts),
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                    });
            }

            return allProducts;
        }


        private async Task<string> GetBuildIdAsync()
        {
            string cachedBuildId = await _cache.GetStringAsync(CacheKey.BUILD_ID);
            if (!string.IsNullOrEmpty(cachedBuildId))
            {
                _logger.LogInformation($"Using cached Coles buildId: {cachedBuildId}");
                return cachedBuildId;
            }

            string url = $"{BaseUrl}/on-special";
            string html = await _retryPolicy.ExecuteAsync(async () =>
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            });

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var scriptNode = doc.DocumentNode.SelectSingleNode("//script[@id='__NEXT_DATA__']");
            if (scriptNode == null)
            {
                _logger.LogError("Could not find __NEXT_DATA__ script in Coles page. HTML content: {0}", html.Substring(0, Math.Min(500, html.Length)));
                throw new Exception("Could not find __NEXT_DATA__ script in Coles page.");
            }

            var json = scriptNode.InnerText;
            try
            {
                using var docJson = JsonDocument.Parse(json);
                var root = docJson.RootElement;

                if (root.TryGetProperty("buildId", out var buildIdElement))
                {
                    string buildId = buildIdElement.GetString();

                    if (string.IsNullOrEmpty(buildId))
                    {
                        throw new Exception("BuildId is null or empty.");
                    }

                    // Cache the buildId
                    await _cache.SetStringAsync(
                        CacheKey.BUILD_ID,
                        buildId,
                        new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60)
                        });

                    _logger.LogInformation($"Fetched new Coles buildId: {buildId}");
                    return buildId;
                }
                else
                {
                    _logger.LogWarning("buildId not found at root level, trying alternative paths...");

                    if (root.TryGetProperty("runtimeConfig", out var runtimeConfig) &&
                        runtimeConfig.TryGetProperty("buildId", out var runtimeBuildIdElement))
                    {
                        string buildId = runtimeBuildIdElement.GetString();

                        if (!string.IsNullOrEmpty(buildId))
                        {
                            await _cache.SetStringAsync(CacheKey.BUILD_ID, buildId,
                                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60) });
                            _logger.LogInformation($"Fetched Coles buildId from runtimeConfig: {buildId}");
                            return buildId;
                        }
                    }

                    var match = Regex.Match(json, @"""buildId""\s*:\s*""([^""]+)""");
                    if (match.Success)
                    {
                        string buildId = match.Groups[1].Value;

                        if (!string.IsNullOrEmpty(buildId))
                        {
                            await _cache.SetStringAsync(CacheKey.BUILD_ID, buildId,
                                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60) });
                            _logger.LogInformation($"Fetched Coles buildId using regex: {buildId}");
                            return buildId;
                        }
                    }

                    throw new Exception("Could not extract buildId from Coles page using any method.");
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse __NEXT_DATA__ JSON. Content: {0}", json);
                throw new Exception("Failed to parse __NEXT_DATA__ JSON.", ex);
            }
        }
    }
}