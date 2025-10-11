using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Polly;
using Polly.Retry;
using PriceCompareCore.Interfaces;
using PriceCompareData.Common;
using PriceCompareData.Data;
using PriceCompareData.DTOs;
using PriceCompareData.Entities.Common;
using PriceCompareData.Entities.History;
using PriceCompareData.Entities.Scraping;

namespace PriceCompareCore.Services
{
    /// <summary>
    /// Woolworths（Playwright 1.40.0）
    /// </summary>
    public class WoolworthsSpecialScraperService : IWoolworthsSpecialScraperService
    {
        private readonly ILogger<WoolworthsSpecialScraperService> _logger;
        private readonly IDistributedCache _cache;
        private readonly AppDbContext _dbContext;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly IIngestionService _ingestion;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private const string SpecialsUrl = "https://www.woolworths.com.au/shop/browse/specials";
        private const string ChromeUa =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36";

        // Woolworths specials API 
        private const string SpecialsApiUrl =
            "https://www.woolworths.com.au/apis/ui/products/360740,307695,237418,35681,35694,218279,31727,123591,36033,158767,105785,384245,686461,320194,96190,752346,252640,511504,33964,763453?excludeUnavailable=true";

        public WoolworthsSpecialScraperService(
        AppDbContext db,
        ILogger<WoolworthsSpecialScraperService> logger,
        IDistributedCache? cache = null)
        {
            _dbContext = db;
            _logger = logger;
            _cache = cache ?? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(2,
                    attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    (ex, ts, i, _) =>
                        _logger.LogWarning(ex, "Playwright attempt {Attempt} failed, will retry.", i + 1));
        }

        public WoolworthsSpecialScraperService(
            ILogger<WoolworthsSpecialScraperService> logger,
            IDistributedCache cache,
            AppDbContext dbContext,
            IIngestionService ingestion)
        {
            _logger = logger;
            _cache = cache;
            _dbContext = dbContext;
            _ingestion = ingestion;

            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(2,
                    attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    (ex, ts, i, _) =>
                        _logger.LogWarning(ex, "Playwright attempt {Attempt} failed, will retry.", i + 1));
        }

        public async Task<List<WoolworthsSpecialProduct>> GetAllOnSpecialProductsAsync(WoolworthsSpecialProductRequest request)
        {
            // 1. Redis
            var cached = await _cache.GetStringAsync(CacheKey.WOOLWORTHS_ON_SPECIAL_PRODUCTS);
            if (!string.IsNullOrEmpty(cached))
            {
                _logger.LogInformation("Using cached Woolworths specials data.");
                var list = JsonSerializer.Deserialize<List<WoolworthsSpecialProduct>>(cached, _jsonOptions) ?? new();
                return request is null ? list : ApplyFilters(list, request);
            }

            var allProducts = new List<WoolworthsSpecialProduct>();

            // 2. Get Api info by Playwright 
            await _retryPolicy.ExecuteAsync(async () =>
            {
                using var pw = await Playwright.CreateAsync();

                await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    Args = new[] { "--disable-dev-shm-usage", "--no-sandbox" }
                });

                await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    Locale = "en-US",
                    UserAgent = ChromeUa
                });

                var page = await context.NewPageAsync();

                _logger.LogInformation("Goto specials page...");
                await page.GotoAsync(SpecialsUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 45000
                });

                // Provide some time for anti-bot scripts to set cookies
                await page.WaitForTimeoutAsync(3000);

                // Send API request dirextly
                _logger.LogInformation("Fetching API: {Url}", SpecialsApiUrl);
                var resp = await page.APIRequest.GetAsync(SpecialsApiUrl);

                _logger.LogWarning("WWS API status={Status}", resp.Status);
                foreach (var header in resp.Headers)
                {
                    _logger.LogWarning("Header {Key}: {Value}", header.Key, header.Value);
                }

                var body = await resp.TextAsync();
                if (string.IsNullOrWhiteSpace(body))
                    throw new Exception("Empty response from Woolworths API");

                // Parse JSON
                var data = JsonSerializer.Deserialize<List<WoolworthsSpecialProductDto>>(body, _jsonOptions);

                if (data != null && data.Count > 0)
                {
                    allProducts.AddRange(data.Select(p => new WoolworthsSpecialProduct
                    {
                        Stockcode = p.Stockcode,
                        Barcode = p.Barcode,
                        DisplayName = p.Name,
                        Brand = p.Brand ?? "",
                        Price = p.Price,
                        WasPrice = p.WasPrice,
                        SavingsAmount = p.SavingsAmount,
                        IsOnSpecial = p.IsOnSpecial,
                        CupPrice = p.CupPrice,
                        CupString = p.CupString,
                        LargeImageFile = p.LargeImageFile
                    }));

                    _logger.LogInformation("Scraped {Count} products from API.", data.Count);
                }
                else
                {
                    throw new Exception("Failed to parse products list from Woolworths API");
                }
            });

            // 3. Store price history
            foreach (var product in allProducts)
            {
                var today = DateTime.UtcNow.Date;
                bool exists = _dbContext.PriceHistory
                    .Any(ph => ph.Name == product.DisplayName &&
                               ph.ScrapedAt >= today &&
                               ph.ScrapedAt < today.AddDays(1));

                if (!exists)
                {
                    var priceHistory = new PriceHistory
                    {
                        Name = product.DisplayName,
                        ImageUrl = product.LargeImageFile,
                        CurrentPrice = product.Price,
                        ScrapedAt = DateTime.UtcNow,
                        OfferType = OfferType.ON_SPECIAL,
                        ShopType = ShopType.WOOLWORTHS
                    };
                    _dbContext.PriceHistory.Add(priceHistory);
                }
            }
            await _dbContext.SaveChangesAsync();
            await _ingestion.UpsertWwsAsync(allProducts);

            // 4. Cache the results
            await _cache.SetStringAsync(
                CacheKey.WOOLWORTHS_ON_SPECIAL_PRODUCTS,
                JsonSerializer.Serialize(allProducts),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
                });

            // 5. Apply filters
            if (request != null)
                allProducts = ApplyFilters(allProducts, request);

            return allProducts;
        }

        private List<WoolworthsSpecialProduct> ApplyFilters(List<WoolworthsSpecialProduct> products, WoolworthsSpecialProductRequest request)
        {
            var q = products.AsQueryable();

            if (!string.IsNullOrWhiteSpace(request.Name))
                q = q.Where(p => !string.IsNullOrEmpty(p.DisplayName)
                                 && p.DisplayName.Contains(request.Name, StringComparison.OrdinalIgnoreCase));

            if (request.MinPrice.HasValue)
                q = q.Where(p => p.Price >= request.MinPrice.Value);

            if (request.MaxPrice.HasValue)
                q = q.Where(p => p.Price <= request.MaxPrice.Value);

            if (request.IsOnSpecial.HasValue)
                q = q.Where(p => p.IsOnSpecial == request.IsOnSpecial.Value);

            return q.ToList();
        }

        internal List<WoolworthsSpecialProduct> ParseProducts(string json)
        {
            var data = JsonSerializer.Deserialize<List<WoolworthsSpecialProductDto>>(json, _jsonOptions);
            return data?.Select(p => new WoolworthsSpecialProduct
            {
                Stockcode = p.Stockcode,
                DisplayName = p.Name,
                Price = p.Price,
                WasPrice = p.WasPrice,
                LargeImageFile = p.LargeImageFile
            }).ToList() ?? new();
        }
    }
}
