using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
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
    /// <summary>
    /// ✅ Coles On-Special scraper powered by Playwright.
    /// 
    /// 关键步骤：
    /// 1️⃣ 启动 headless Chromium（Lambda 兼容）。
    /// 2️⃣ 打开 https://www.coles.com.au/on-special 页面，触发反爬验证脚本。
    /// 3️⃣ 从页面 DOM 中提取 __NEXT_DATA__.buildId。
    /// 4️⃣ 访问 JSON 接口 /_next/data/{buildId}/en/on-special.json?page={n}。
    /// 5️⃣ 解析结果 → 缓存 → 写入数据库 → 调用 ingestion 更新产品表。
    /// </summary>
    public class ColesSpecialScraperService : IColesSpecialScraperService
    {
        private readonly ILogger<ColesSpecialScraperService> _logger;
        private readonly IDistributedCache _cache;
        private readonly AppDbContext _db;
        private readonly IIngestionService _ingestion;

        private readonly AsyncRetryPolicy _retry;
        private static readonly JsonSerializerOptions _jsonOpt = new() { PropertyNameCaseInsensitive = true };

        private const string BaseUrl = WebInfo.COLES_BASE_URL; // "https://www.coles.com.au"
        private const string SpecialsPath = "/on-special";

        public ColesSpecialScraperService(
            ILogger<ColesSpecialScraperService> logger,
            IDistributedCache cache,
            AppDbContext dbContext,
            IIngestionService ingestion)
        {
            _logger = logger;
            _cache = cache;
            _db = dbContext;
            _ingestion = ingestion;

            // ✅ Retry 策略：失败后 1s, 2s 重试
            _retry = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(2, i => TimeSpan.FromSeconds(Math.Pow(2, i)),
                    (ex, ts, i, _) =>
                        _logger.LogWarning(ex, "[Retry] attempt {Attempt} after {Delay}s.", i + 1, ts.TotalSeconds));
        }

        public async Task<List<ColesSpecialProduct>> GetAllOnSpecialProductsAsync(ColesSpecialProductRequest request = null)
        {
            // ✅ Step 1: 优先读取缓存
            var cached = await _cache.GetStringAsync(CacheKey.COLES_ON_SPECIAL_PRODUCTS);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                _logger.LogInformation("[Cache] Using cached Coles specials.");
                var list = JsonSerializer.Deserialize<List<ColesSpecialProduct>>(cached, _jsonOpt) ?? new();
                return request is null ? list : ApplyFilters(list, request);
            }

            var all = new List<ColesSpecialProduct>();

            // ✅ Step 2: 启动 Playwright，模拟真实浏览器
            await _retry.ExecuteAsync(async () =>
            {
                using var pw = await Playwright.CreateAsync();
                await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    Args = new[]
                    {
                        "--no-sandbox",
                        "--disable-dev-shm-usage",
                        "--disable-gpu",
                        "--disable-setuid-sandbox"
                    }
                });

                await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                                "Chrome/124.0.0.0 Safari/537.36",
                    Locale = "en-AU"
                });

                var page = await context.NewPageAsync();

                var specialsUrl = $"{BaseUrl}{SpecialsPath}";
                _logger.LogInformation("Navigating to {Url}", specialsUrl);

                await page.GotoAsync(specialsUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 45000
                });

                // 等待反爬脚本执行完成
                await page.WaitForTimeoutAsync(2000);

                // ✅ Step 3: 获取 buildId
                var buildId = await GetBuildIdFromPageAsync(page);
                _logger.LogInformation("Fetched buildId: {BuildId}", buildId);

                // ✅ Step 4: 分页爬取 JSON
                int pageNo = 1;
                while (true)
                {
                    var dataUrl = $"{BaseUrl}/_next/data/{buildId}/en/on-special.json?page={pageNo}";
                    _logger.LogInformation("Fetching JSON page {Page}: {Url}", pageNo, dataUrl);

                    var resp = await page.APIRequest.GetAsync(dataUrl);
                    if (!resp.Ok)
                    {
                        _logger.LogWarning("Non-OK ({Status}) for {Url}", resp.Status, dataUrl);
                        break;
                    }

                    var text = await resp.TextAsync();
                    if (text.TrimStart().StartsWith("<"))
                    {
                        _logger.LogWarning("HTML returned for JSON URL (anti-bot triggered). Stop scraping.");
                        break;
                    }

                    var dto = JsonSerializer.Deserialize<ColesApiResponse>(text, _jsonOpt);
                    var results = dto?.PageProps?.SearchResults?.Results;

                    if (results == null || results.Count == 0)
                    {
                        _logger.LogInformation("No results on page {Page}, stop.", pageNo);
                        break;
                    }

                    var mapped = results
                        .Where(r => r._type.Equals("PRODUCT", StringComparison.OrdinalIgnoreCase))
                        .Select(r => new ColesSpecialProduct
                        {
                            Id = (int)r.Id,
                            Name = r.Name,
                            CurrentPrice = r.Pricing?.Now ?? 0m,
                            OriginalPrice = r.Pricing?.Was ?? 0m,
                            PricePerUnit = r.Pricing?.Comparable,
                            ImageUrl = r.ImageUris?.FirstOrDefault()?.Uri,
                            IsSponsored = false,
                            ScrapedAt = DateTime.UtcNow
                        })
                        .ToList();

                    _logger.LogInformation("Page {Page} -> {Count} products.", pageNo, mapped.Count);
                    all.AddRange(mapped);
                    pageNo++;
                }
            });

            // ✅ Step 5: 写入价格历史
            foreach (var p in all)
            {
                var today = DateTime.UtcNow.Date;
                bool exists = _db.PriceHistory.Any(ph =>
                    ph.Name == p.Name &&
                    ph.ShopType == ShopType.COLES &&
                    ph.OfferType == OfferType.ON_SPECIAL &&
                    ph.ScrapedAt >= today && ph.ScrapedAt < today.AddDays(1));

                if (!exists)
                {
                    _db.PriceHistory.Add(new PriceHistory
                    {
                        Name = p.Name,
                        ImageUrl = p.ImageUrl,
                        CurrentPrice = p.CurrentPrice,
                        ScrapedAt = DateTime.UtcNow,
                        OfferType = OfferType.ON_SPECIAL,
                        ShopType = ShopType.COLES
                    });
                }
            }
            await _db.SaveChangesAsync();

            // ✅ Step 6: Upsert product base table
            await _ingestion.UpsertColesSpecialAsync(all);

            // ✅ Step 7: Cache result
            await _cache.SetStringAsync(
                CacheKey.COLES_ON_SPECIAL_PRODUCTS,
                JsonSerializer.Serialize(all),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60)
                });

            // ✅ Step 8: Filtering
            if (request != null)
                all = ApplyFilters(all, request);

            return all;
        }

        /// <summary>
        /// Extracts Next.js buildId from __NEXT_DATA__ script after page JS execution.
        /// </summary>
        private async Task<string> GetBuildIdFromPageAsync(IPage page)
        {
            var node = await page.QuerySelectorAsync("script#__NEXT_DATA__");
            if (node == null)
            {
                _logger.LogError("[ParseError] Missing __NEXT_DATA__ element in Coles page.");
                throw new Exception("Could not find __NEXT_DATA__ script in Coles page.");
            }

            var json = await node.InnerTextAsync();
            if (string.IsNullOrWhiteSpace(json))
                throw new Exception("__NEXT_DATA__ content is empty.");

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("buildId", out var buildIdProp))
                throw new Exception("buildId not found in __NEXT_DATA__.");

            var buildId = buildIdProp.GetString();
            if (string.IsNullOrWhiteSpace(buildId))
                throw new Exception("buildId is null/empty.");

            return buildId!;
        }

        private static List<ColesSpecialProduct> ApplyFilters(List<ColesSpecialProduct> src, ColesSpecialProductRequest req)
        {
            var q = src.AsQueryable();
            if (!string.IsNullOrWhiteSpace(req.Name))
                q = q.Where(p => !string.IsNullOrEmpty(p.Name) &&
                                 p.Name.Contains(req.Name, StringComparison.OrdinalIgnoreCase));
            if (req.IsSponsored)
                q = q.Where(p => p.IsSponsored);
            if (req.MinPrice.HasValue)
                q = q.Where(p => p.CurrentPrice >= req.MinPrice.Value);
            if (req.MaxPrice.HasValue)
                q = q.Where(p => p.CurrentPrice <= req.MaxPrice.Value);
            return q.ToList();
        }
    }

    #region DTOs (Coles JSON)
    public class ColesApiResponse
    {
        public ColesPageProps PageProps { get; set; } = new();
    }

    public class ColesPageProps
    {
        public ColesSearchResults SearchResults { get; set; } = new();
    }

    public class ColesSearchResults
    {
        public List<ColesSearchItem> Results { get; set; } = new();
    }

    public class ColesSearchItem
    {
        public string _type { get; set; } = string.Empty;
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public ColesPricing? Pricing { get; set; }
        public List<ColesImage>? ImageUris { get; set; }
    }

    public class ColesPricing
    {
        public decimal? Now { get; set; }
        public decimal? Was { get; set; }
        public string? Comparable { get; set; }
    }

    public class ColesImage
    {
        public string? Uri { get; set; }
    }
    #endregion
}
