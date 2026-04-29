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
using Microsoft.EntityFrameworkCore;
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
        private readonly IScrapeExportService _export;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private const string SpecialsUrl = "https://www.woolworths.com.au/shop/browse/specials";
        private const string DefaultUa =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

        // Woolworths specials API 
        private const string SpecialsApiUrl =
            "https://www.woolworths.com.au/apis/ui/products/360740,307695,237418,35681,35694,218279,31727,123591,36033,158767,105785,384245,686461,320194,96190,752346,252640,511504,33964,763453?excludeUnavailable=true";

        public WoolworthsSpecialScraperService(
        AppDbContext db,
        ILogger<WoolworthsSpecialScraperService> logger,
        IDistributedCache? cache = null,
        IScrapeExportService? export = null)
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
            _export = export ?? new NoopScrapeExportService();
        }

        public WoolworthsSpecialScraperService(
            ILogger<WoolworthsSpecialScraperService> logger,
            IDistributedCache cache,
            AppDbContext dbContext,
            IIngestionService ingestion,
            IScrapeExportService export)
        {
            _logger = logger;
            _cache = cache;
            _dbContext = dbContext;
            _ingestion = ingestion;
            _export = export;

            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(2,
                    attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    (ex, ts, i, _) =>
                        _logger.LogWarning(ex, "Playwright attempt {Attempt} failed, will retry.", i + 1));
        }

        public async Task<List<WoolworthsSpecialProduct>> GetAllOnSpecialProductsAsync(WoolworthsSpecialProductRequest request)
        {
            var probeOnly = GetProbeOnly();

            // 1. Redis
            if (!probeOnly)
            {
                var cached = await _cache.GetStringAsync(CacheKey.WOOLWORTHS_ON_SPECIAL_PRODUCTS);
                if (!string.IsNullOrEmpty(cached))
                {
                    _logger.LogInformation("Using cached Woolworths specials data.");
                    var list = JsonSerializer.Deserialize<List<WoolworthsSpecialProduct>>(cached, _jsonOptions) ?? new();
                    return request is null ? list : ApplyFilters(list, request);
                }
            }

            var allProducts = new List<WoolworthsSpecialProduct>();
            var scrapedAt = DateTime.UtcNow;
            var apiUrl = BuildSpecialsApiUrl();
            WwsProbeResult? probeResult = null;

            // 2. Get Api info by Playwright 
            await _retryPolicy.ExecuteAsync(async () =>
            {
                using var pw = await Playwright.CreateAsync();

                await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = GetHeadless(),
                    SlowMo = GetSlowMoMs(),
                    Channel = GetChromeChannel(),
                    Args = new[]
                    {
                        "--disable-dev-shm-usage",
                        "--no-sandbox",
                        "--disable-gpu",
                        "--disable-features=IsolateOrigins,site-per-process",
                        "--disable-blink-features=AutomationControlled"
                    }
                });

                await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    Locale = "en-AU",
                    UserAgent = GetUserAgent(),
                    TimezoneId = "Australia/Sydney",
                    ViewportSize = new ViewportSize { Width = 1365, Height = 768 },
                    ScreenSize = new ScreenSize { Width = 1365, Height = 768 },
                    DeviceScaleFactor = 1,
                    IsMobile = false,
                    HasTouch = false,
                    ExtraHTTPHeaders = new Dictionary<string, string>
                    {
                        ["Accept-Language"] = "en-AU,en;q=0.9",
                        ["DNT"] = "1"
                    }
                });

                await context.AddInitScriptAsync(GetStealthScript());

                var page = await context.NewPageAsync();
                var apiHits = new List<ApiHit>();
                var apiLock = new object();
                page.Response += (_, resp) =>
                {
                    if (resp.Url.Contains("/apis/ui/products/", StringComparison.OrdinalIgnoreCase))
                    {
                        lock (apiLock)
                        {
                            apiHits.Add(new ApiHit(resp.Status, resp.Url));
                        }
                    }
                };

                _logger.LogInformation("Goto specials page...");
                await page.GotoAsync(SpecialsUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60000
                });

                // Provide some time for anti-bot scripts to set cookies + simulate user
                await HumanDelayAsync(page, 1200, 2200);
                await TryClickAsync(page, "button:has-text(\"Accept\")", 1500);
                await HumanDelayAsync(page, 800, 1400);
                await page.Mouse.MoveAsync(250, 250);
                await page.Mouse.WheelAsync(0, 1200);
                await HumanDelayAsync(page, 600, 1200);
                await page.Mouse.WheelAsync(0, 1200);
                await HumanDelayAsync(page, 600, 1200);

                if (probeOnly)
                {
                    var firstSelector = await WaitForAnySelectorAsync(
                        page,
                        new[]
                        {
                            "a[href*='/shop/productdetails/']",
                            "[data-testid*='product-tile']",
                            "[data-testid*='product']"
                        },
                        20000);
                    if (firstSelector is null)
                    {
                        _logger.LogWarning("WWS PROBE: no product selector detected within timeout.");
                    }
                    else
                    {
                        _logger.LogInformation("WWS PROBE: first product selector matched: {Selector}", firstSelector);
                    }
                }

                var cookies = await context.CookiesAsync(new[] { SpecialsUrl });
                if (cookies.Count > 0)
                {
                    _logger.LogInformation("WWS cookies: {Names}", string.Join(", ", cookies.Select(c => c.Name)));
                }

                if (probeOnly)
                {
                    probeResult = await ProbePageAsync(page, apiHits);
                    return;
                }

                // Fetch API inside page context (closer to real browser behavior)
                _logger.LogInformation("Fetching API via in-page fetch: {Url}", apiUrl);
                var resp = await page.EvaluateAsync<ApiProbeResponse>(
                    @"async (url) => {
                        const res = await fetch(url, { credentials: 'include' });
                        const text = await res.text();
                        const headers = {};
                        res.headers.forEach((v, k) => { headers[k] = v; });
                        return { Status: res.status, Ok: res.ok, Url: res.url, Text: text, Headers: headers };
                    }",
                    apiUrl);

                _logger.LogWarning("WWS API status={Status} ok={Ok} url={Url}", resp.Status, resp.Ok, resp.Url);
                if (resp.Headers != null)
                {
                    foreach (var header in resp.Headers)
                    {
                        _logger.LogWarning("Header {Key}: {Value}", header.Key, header.Value);
                    }
                }

                var body = resp.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(body))
                    throw new Exception("Empty response from Woolworths API");

                if (IsAccessDeniedBody(body))
                {
                    var snippet = body.Length > 500 ? body[..500] : body;
                    _logger.LogWarning("WWS API returned HTML/AccessDenied. Snippet: {Snippet}", snippet);
                    throw new Exception("WWS API returned HTML/AccessDenied.");
                }

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
                        LargeImageFile = p.LargeImageFile,
                        ScrapedAt = scrapedAt
                    }));

                    _logger.LogInformation("Scraped {Count} products from API.", data.Count);
                }
                else
                {
                    throw new Exception("Failed to parse products list from Woolworths API");
                }
            });

            if (probeOnly)
            {
                if (probeResult != null)
                {
                    LogProbeResult(probeResult);
                }
                else
                {
                    _logger.LogWarning("WWS PROBE: no result (Playwright failed before probe).");
                }

                return new List<WoolworthsSpecialProduct>();
            }

            // 3. Store price history
            var today = scrapedAt.Date;
            var tomorrow = today.AddDays(1);
            var existingNames = new HashSet<string>(
                await _dbContext.PriceHistory
                    .Where(ph => ph.ShopType == ShopType.WOOLWORTHS &&
                                 ph.OfferType == OfferType.ON_SPECIAL &&
                                 ph.ScrapedAt >= today && ph.ScrapedAt < tomorrow)
                    .Select(ph => ph.Name!)
                    .ToListAsync(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var product in allProducts)
            {
                if (existingNames.Contains(product.DisplayName ?? string.Empty)) continue;
                _dbContext.PriceHistory.Add(new PriceHistory
                {
                    Name = product.DisplayName,
                    ImageUrl = product.LargeImageFile ?? string.Empty,
                    CurrentPrice = product.Price,
                    ScrapedAt = scrapedAt,
                    OfferType = OfferType.ON_SPECIAL,
                    ShopType = ShopType.WOOLWORTHS
                });
                existingNames.Add(product.DisplayName ?? string.Empty);
            }
            await _dbContext.SaveChangesAsync();
            await _ingestion.UpsertWwsAsync(allProducts);
            var productRows = _ingestion.MapWoolworthsProducts(allProducts);
            var priceHistoryRows = allProducts.Select(p => new PriceHistory
            {
                Name = p.DisplayName,
                ImageUrl = p.LargeImageFile ?? string.Empty,
                CurrentPrice = p.Price,
                ScrapedAt = scrapedAt,
                OfferType = OfferType.ON_SPECIAL,
                ShopType = ShopType.WOOLWORTHS
            }).ToList();
            await _export.ExportAsync(
                new ScrapeExportRequest(
                    "woolworths_special",
                    scrapedAt,
                    priceHistoryRows,
                    productRows));

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
                LargeImageFile = p.LargeImageFile,
                ScrapedAt = DateTime.UtcNow
            }).ToList() ?? new();
        }

        private class NoopScrapeExportService : IScrapeExportService
        {
            public Task<ScrapeExportResult?> ExportAsync(ScrapeExportRequest request, CancellationToken ct = default)
                => Task.FromResult<ScrapeExportResult?>(null);
        }

        private static string GetUserAgent()
        {
            var ua = Environment.GetEnvironmentVariable("WWS_USER_AGENT");
            return string.IsNullOrWhiteSpace(ua) ? DefaultUa : ua.Trim();
        }

        private static string BuildSpecialsApiUrl()
        {
            var ids = Environment.GetEnvironmentVariable("WWS_PRODUCT_IDS");
            if (!string.IsNullOrWhiteSpace(ids))
            {
                return $"https://www.woolworths.com.au/apis/ui/products/{ids}?excludeUnavailable=true";
            }

            var overrideUrl = Environment.GetEnvironmentVariable("WWS_SPECIALS_API_URL");
            return string.IsNullOrWhiteSpace(overrideUrl) ? SpecialsApiUrl : overrideUrl.Trim();
        }

        private static int GetSlowMoMs()
        {
            var raw = Environment.GetEnvironmentVariable("WWS_SLOWMO_MS");
            return int.TryParse(raw, out var ms) && ms >= 0 ? ms : 60;
        }

        private static bool GetHeadless()
        {
            var headful = Environment.GetEnvironmentVariable("WWS_HEADFUL");
            return !string.Equals(headful, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetChromeChannel()
        {
            var useChrome = Environment.GetEnvironmentVariable("WWS_USE_CHROME_CHANNEL");
            return string.Equals(useChrome, "true", StringComparison.OrdinalIgnoreCase) ? "chrome" : null;
        }

        private static bool IsAccessDeniedBody(string body)
        {
            var trimmed = body.TrimStart();
            if (trimmed.StartsWith("<"))
            {
                return true;
            }

            return body.Contains("Access Denied", StringComparison.OrdinalIgnoreCase) ||
                   body.Contains("Request blocked", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<string?> WaitForAnySelectorAsync(IPage page, IReadOnlyList<string> selectors, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                foreach (var selector in selectors)
                {
                    try
                    {
                        var count = await page.Locator(selector).CountAsync();
                        if (count > 0)
                        {
                            return selector;
                        }
                    }
                    catch
                    {
                        // ignore and keep polling
                    }
                }

                await page.WaitForTimeoutAsync(500);
            }

            return null;
        }

        private static async Task HumanDelayAsync(IPage page, int minMs, int maxMs)
        {
            if (minMs < 0) minMs = 0;
            if (maxMs < minMs) maxMs = minMs;
            var delay = Random.Shared.Next(minMs, maxMs + 1);
            await page.WaitForTimeoutAsync(delay);
        }

        private static async Task<bool> TryClickAsync(IPage page, string selector, int timeoutMs)
        {
            try
            {
                await page.ClickAsync(selector, new PageClickOptions { Timeout = timeoutMs });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private sealed class ApiProbeResponse
        {
            public int Status { get; set; }
            public bool Ok { get; set; }
            public string? Url { get; set; }
            public string? Text { get; set; }
            public Dictionary<string, string>? Headers { get; set; }
        }

        private static string GetStealthScript()
        {
            return @"
// webdriver
Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
// languages
Object.defineProperty(navigator, 'languages', { get: () => ['en-AU', 'en'] });
// plugins
Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] });
// chrome runtime
window.chrome = window.chrome || { runtime: {} };
// permissions
const originalQuery = window.navigator.permissions.query;
window.navigator.permissions.query = (parameters) => (
  parameters.name === 'notifications'
    ? Promise.resolve({ state: Notification.permission })
    : originalQuery(parameters)
);
";
        }

        private static bool GetProbeOnly()
        {
            var raw = Environment.GetEnvironmentVariable("WWS_PROBE_ONLY");
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<WwsProbeResult> ProbePageAsync(IPage page, IReadOnlyList<ApiHit> apiHits)
        {
            await HumanDelayAsync(page, 800, 1400);
            await page.Mouse.WheelAsync(0, 1400);
            await HumanDelayAsync(page, 800, 1400);

            var productLinks = await page.Locator("a[href*='/shop/productdetails/']").CountAsync();
            var productTiles = await page.Locator("[data-testid*='product-tile']").CountAsync();
            var productTestIds = await page.Locator("[data-testid*='product']").CountAsync();

            var html = await page.ContentAsync();
            var denied = html.Contains("Access Denied", StringComparison.OrdinalIgnoreCase) ||
                         html.Contains("Request blocked", StringComparison.OrdinalIgnoreCase);
            var snippet = denied ? html[..Math.Min(500, html.Length)] : null;

            var apiTotal = apiHits.Count;
            var api403 = apiHits.Count(h => h.Status == 403);

            return new WwsProbeResult(
                productLinks,
                productTiles,
                productTestIds,
                apiTotal,
                api403,
                denied,
                snippet);
        }

        private void LogProbeResult(WwsProbeResult result)
        {
            _logger.LogInformation(
                "WWS PROBE: links={Links} tiles={Tiles} testIdProducts={TestIds} apiRequests={ApiTotal} api403={Api403} accessDeniedHtml={Denied}",
                result.ProductLinkCount,
                result.ProductTileCount,
                result.ProductTestIdCount,
                result.ApiRequestCount,
                result.Api403Count,
                result.AccessDeniedInHtml);

            if (result.AccessDeniedInHtml && !string.IsNullOrWhiteSpace(result.HtmlSnippet))
            {
                _logger.LogWarning("WWS PROBE: HTML snippet: {Snippet}", result.HtmlSnippet);
            }
        }

        private sealed record ApiHit(int Status, string Url);

        private sealed record WwsProbeResult(
            int ProductLinkCount,
            int ProductTileCount,
            int ProductTestIdCount,
            int ApiRequestCount,
            int Api403Count,
            bool AccessDeniedInHtml,
            string? HtmlSnippet);
    }
}
