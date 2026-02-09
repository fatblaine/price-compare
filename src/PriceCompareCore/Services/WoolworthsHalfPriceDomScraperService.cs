using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using PriceCompareCore.Interfaces;
using PriceCompareData.Common;
using PriceCompareData.Data;
using PriceCompareData.Entities.Common;
using PriceCompareData.Entities.History;
using PriceCompareData.Entities.Scraping;

namespace PriceCompareCore.Services
{
    public class WoolworthsHalfPriceDomScraperService : IWoolworthsHalfPriceDomScraperService
    {
        private const string HalfPriceUrl = "https://www.woolworths.com.au/shop/browse/specials/half-price";
        private const string DefaultUa =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

        private readonly ILogger<WoolworthsHalfPriceDomScraperService> _logger;
        private readonly IDistributedCache _cache;
        private readonly AppDbContext _dbContext;
        private readonly IIngestionService _ingestion;
        private readonly IScrapeExportService _export;

        public WoolworthsHalfPriceDomScraperService(
            ILogger<WoolworthsHalfPriceDomScraperService> logger,
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
        }

        public async Task<List<WoolworthsSpecialProduct>> ScrapeAsync(int limit = 0, CancellationToken ct = default)
        {
            var hardCap = GetDomHardCap();
            if (limit <= 0) limit = hardCap;
            if (limit > hardCap) limit = hardCap;

            var cached = await _cache.GetStringAsync(CacheKey.WOOLWORTHS_HALF_PRICE_PRODUCTS, ct);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                _logger.LogInformation("WWS DOM: returning cached half-price products.");
                var cachedItems = JsonSerializer.Deserialize<List<WoolworthsSpecialProduct>>(cached) ?? new();
                return cachedItems.Take(limit).ToList();
            }

            var fetchLimit = hardCap;

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
            var domItems = new List<DomProduct>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var maxPages = GetMaxPages();
            var startPage = GetStartPage();

            for (var pageNumber = startPage; pageNumber <= maxPages && domItems.Count < fetchLimit; pageNumber++)
            {
                var url = BuildHalfPriceUrl(pageNumber);
                _logger.LogInformation("WWS DOM: goto half-price page {Page}...", pageNumber);
                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60000
                });

                await HumanDelayAsync(page, 1200, 2200);
                if (pageNumber == startPage)
                {
                    await TryClickAsync(page, "button:has-text(\"Accept\")", 1500);
                }
                await HumanDelayAsync(page, 800, 1400);
                await page.Mouse.MoveAsync(250, 250);
                if (pageNumber == startPage)
                {
                    await TryDismissShopModalAsync(page);
                }

                var selector = await WaitForAnySelectorAsync(
                    page,
                    new[]
                    {
                        "a[href*='/shop/productdetails/']",
                        "[data-testid*='product-tile']",
                        "[data-testid*='product']"
                    },
                    20000);

                if (selector is null)
                {
                    _logger.LogWarning("WWS DOM: no product selector detected on page {Page}.", pageNumber);
                    break;
                }

                var remaining = fetchLimit - domItems.Count;
                var pageItems = await ExtractDomItemsAsync(page, remaining, ct, seen, GetMaxScrollRounds());
                _logger.LogInformation("WWS DOM: page {Page} -> {Count} items.", pageNumber, pageItems.Count);

                if (pageItems.Count == 0)
                {
                    break;
                }

                domItems.AddRange(pageItems);
            }

            var scrapedAt = DateTime.UtcNow;
            var mapped = MapDomItems(domItems, scrapedAt);
            _logger.LogInformation("WWS DOM: extracted {Count} products.", mapped.Count);

            var serialized = JsonSerializer.Serialize(mapped);
            await _cache.SetStringAsync(
                CacheKey.WOOLWORTHS_HALF_PRICE_PRODUCTS,
                serialized,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60)
                },
                ct);

            await PersistAndExportAsync(mapped, scrapedAt, ct);

            return mapped.Take(limit).ToList();
        }

        private async Task PersistAndExportAsync(
            IReadOnlyList<WoolworthsSpecialProduct> products,
            DateTime scrapedAt,
            CancellationToken ct)
        {
            if (products.Count == 0)
            {
                return;
            }

            var today = scrapedAt.Date;
            var priceHistoryRows = new List<PriceHistory>(products.Count);

            foreach (var product in products)
            {
                var exists = _dbContext.PriceHistory.Any(ph =>
                    ph.Name == product.DisplayName &&
                    ph.ShopType == ShopType.WOOLWORTHS &&
                    ph.OfferType == OfferType.HALF_PRICE &&
                    ph.ScrapedAt >= today &&
                    ph.ScrapedAt < today.AddDays(1));

                var priceHistory = new PriceHistory
                {
                    Name = product.DisplayName,
                    ImageUrl = product.LargeImageFile ?? string.Empty,
                    CurrentPrice = product.Price,
                    ScrapedAt = scrapedAt,
                    OfferType = OfferType.HALF_PRICE,
                    ShopType = ShopType.WOOLWORTHS
                };

                priceHistoryRows.Add(priceHistory);

                if (!exists)
                {
                    _dbContext.PriceHistory.Add(priceHistory);
                }
            }

            await _dbContext.SaveChangesAsync(ct);
            await _ingestion.UpsertWwsAsync(products);
            var productRows = _ingestion.MapWoolworthsProducts(products);
            await _export.ExportAsync(
                new ScrapeExportRequest(
                    "woolworths_half_price",
                    scrapedAt,
                    priceHistoryRows,
                    productRows),
                ct);
        }

        private static List<WoolworthsSpecialProduct> MapDomItems(IReadOnlyList<DomProduct> items, DateTime scrapedAt)
        {
            var result = new List<WoolworthsSpecialProduct>();
            foreach (var item in items)
            {
                var name = ExtractNameFromAria(item.Aria);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = item.Name?.Trim() ?? string.Empty;
                }

                if (!IsValidName(name))
                {
                    continue;
                }

                var price = ExtractPriceFromAria(item.Aria, name);
                if (!price.HasValue)
                {
                    continue;
                }

                var was = ParseMoney(ExtractFromAria(item.Aria, "Was"));
                var savings = ExtractSavingsFromAria(item.Aria);
                if (was.HasValue && !savings.HasValue && was.Value > price.Value)
                {
                    savings = was.Value - price.Value;
                }
                if (!was.HasValue && savings.HasValue && price.HasValue)
                {
                    was = price.Value + savings.Value;
                }

                result.Add(new WoolworthsSpecialProduct
                {
                    Stockcode = 0,
                    Barcode = string.Empty,
                    DisplayName = name,
                    Brand = string.Empty,
                    Price = price.Value,
                    WasPrice = was,
                    SavingsAmount = savings,
                    IsOnSpecial = true,
                    CupPrice = null,
                    CupString = null,
                    LargeImageFile = item.Image,
                    ScrapedAt = scrapedAt
                });
            }

            return result;
        }

        private static decimal? ExtractPriceFromAria(string? aria, string? name)
        {
            if (string.IsNullOrWhiteSpace(aria))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                var idx = aria.IndexOf(name, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var tail = aria.Substring(idx + name.Length);
                    var price = ParseFirstMoney(tail);
                    if (price.HasValue)
                    {
                        return price;
                    }
                }
            }

            var matches = Regex.Matches(aria, @"\$\s*(\d+(?:\.\d+)?)");
            if (matches.Count == 0)
            {
                return null;
            }

            foreach (Match match in matches)
            {
                if (!match.Success)
                {
                    continue;
                }

                var beforeStart = Math.Max(0, match.Index - 12);
                var before = aria.Substring(beforeStart, match.Index - beforeStart);
                if (before.IndexOf("Save", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }
                if (before.IndexOf("Was", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                var after = aria.Substring(match.Index + match.Length);
                if (Regex.IsMatch(after, @"^\s*(/|per\b)", RegexOptions.IgnoreCase))
                {
                    continue;
                }

                return ParseMoney(match.Groups[1].Value);
            }

            if (matches.Count >= 2 && aria.IndexOf("Save", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ParseMoney(matches[1].Groups[1].Value);
            }

            return ParseMoney(matches[0].Groups[1].Value);
        }

        private static decimal? ExtractSavingsFromAria(string? aria)
        {
            if (string.IsNullOrWhiteSpace(aria))
            {
                return null;
            }

            var match = Regex.Match(aria, @"Save\s+\$?\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            return ParseMoney(match.Groups[1].Value);
        }

        private static decimal? ParseMoney(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var cleaned = raw.Replace("Was", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Save", "", StringComparison.OrdinalIgnoreCase)
                .Replace("$", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" ", "");

            return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        private static decimal? ParseFirstMoney(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var match = Regex.Match(raw, @"\$\s*(\d+(?:\.\d+)?)");
            if (!match.Success)
            {
                return null;
            }

            return ParseMoney(match.Groups[1].Value);
        }

        private static string? ExtractFromAria(string? aria, string key)
        {
            if (string.IsNullOrWhiteSpace(aria))
            {
                return null;
            }

            var marker = key + " ";
            var idx = aria.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return null;
            }

            var start = idx + marker.Length;
            var end = aria.IndexOf(',', start);
            if (end < 0)
            {
                end = aria.Length;
            }

            var segment = aria.Substring(start, end - start).Trim();
            return segment.StartsWith("$", StringComparison.OrdinalIgnoreCase) ? segment : "$" + segment;
        }

        private static async Task<List<DomProduct>> ExtractDomItemsAsync(
            IPage page,
            int limit,
            CancellationToken ct,
            HashSet<string> seen,
            int maxRounds)
        {
            var results = new List<DomProduct>();

            var locator = page.Locator("a[href*='/shop/productdetails/'][aria-label]");
            var stableRounds = 0;

            while (results.Count < limit && stableRounds < 3 && maxRounds-- > 0)
            {
                var before = results.Count;
                var total = await locator.CountAsync();

                for (var i = 0; i < total && results.Count < limit; i++)
                {
                    if (ct.IsCancellationRequested)
                    {
                        return results;
                    }

                    var anchor = locator.Nth(i);
                    var href = (await anchor.GetAttributeAsync("href")) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(href))
                    {
                        continue;
                    }

                    var fullHref = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? href
                        : new Uri(new Uri(HalfPriceUrl), href).ToString();

                    if (!seen.Add(fullHref))
                    {
                        continue;
                    }

                    var aria = (await anchor.GetAttributeAsync("aria-label")) ?? string.Empty;
                    if (!IsProductAria(aria))
                    {
                        continue;
                    }

                    var name = ExtractNameFromAria(aria);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = (await anchor.InnerTextAsync()).Trim();
                    }

                    string image = string.Empty;
                    var img = anchor.Locator("img");
                    if (await img.CountAsync() > 0)
                    {
                        image = (await img.First.GetAttributeAsync("src"))
                                ?? (await img.First.GetAttributeAsync("data-src"))
                                ?? string.Empty;
                    }

                    results.Add(new DomProduct
                    {
                        Name = name,
                        Href = fullHref,
                        Image = image,
                        Aria = aria
                    });
                }

                if (results.Count == before)
                {
                    stableRounds++;
                }
                else
                {
                    stableRounds = 0;
                }

                if (results.Count >= limit)
                {
                    break;
                }

                await page.EvaluateAsync("() => window.scrollBy(0, document.body.scrollHeight)");
                await HumanDelayAsync(page, 700, 1200);
            }

            return results;
        }

        private static string GetUserAgent()
        {
            var ua = Environment.GetEnvironmentVariable("WWS_USER_AGENT");
            return string.IsNullOrWhiteSpace(ua) ? DefaultUa : ua.Trim();
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

        private static async Task HumanDelayAsync(IPage page, int minMs, int maxMs)
        {
            if (minMs < 0) minMs = 0;
            if (maxMs < minMs) maxMs = minMs;
            var delay = Random.Shared.Next(minMs, maxMs + 1);
            await page.WaitForTimeoutAsync(delay);
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

        private static async Task TryDismissShopModalAsync(IPage page)
        {
            var modalTitle = page.Locator("text=How would you like to shop?");
            if (await modalTitle.CountAsync() == 0)
            {
                return;
            }

            await TryClickAsync(page, "button[aria-label='Close']", 1500);
            await TryClickAsync(page, "button:has-text('Close')", 1500);

            await TryClickAsync(page, "text=Delivery", 1500);
            await TryClickAsync(page, "button:has-text('Save & continue')", 2000);

            try
            {
                await page.Mouse.ClickAsync(50, 50);
            }
            catch
            {
                // ignore
            }

            try
            {
                await modalTitle.First.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Hidden,
                    Timeout = 8000
                });
            }
            catch
            {
                // ignore - modal may persist, we will still try selectors
            }
        }

        private static string GetStealthScript()
        {
            return @"
Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
Object.defineProperty(navigator, 'languages', { get: () => ['en-AU', 'en'] });
Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] });
window.chrome = window.chrome || { runtime: {} };
const originalQuery = window.navigator.permissions.query;
window.navigator.permissions.query = (parameters) => (
  parameters.name === 'notifications'
    ? Promise.resolve({ state: Notification.permission })
    : originalQuery(parameters)
);
";
        }

        private static int GetDomHardCap()
        {
            var raw = Environment.GetEnvironmentVariable("WWS_DOM_MAX_ITEMS");
            return int.TryParse(raw, out var v) && v > 0 ? v : 2000;
        }

        private static int GetMaxScrollRounds()
        {
            var raw = Environment.GetEnvironmentVariable("WWS_DOM_MAX_ROUNDS");
            return int.TryParse(raw, out var v) && v > 0 ? v : 6;
        }

        private static int GetMaxPages()
        {
            var raw = Environment.GetEnvironmentVariable("WWS_DOM_MAX_PAGES");
            return int.TryParse(raw, out var v) && v > 0 ? v : 100;
        }

        private static int GetStartPage()
        {
            var raw = Environment.GetEnvironmentVariable("WWS_DOM_START_PAGE");
            return int.TryParse(raw, out var v) && v > 0 ? v : 1;
        }

        private static string BuildHalfPriceUrl(int pageNumber)
            => $"{HalfPriceUrl}?pageNumber={pageNumber}";

        private sealed class DomProduct
        {
            public string? Name { get; set; }
            public string? Href { get; set; }
            public string? Image { get; set; }
            public string? Aria { get; set; }
        }

        private static bool IsProductAria(string? aria)
        {
            if (string.IsNullOrWhiteSpace(aria))
            {
                return false;
            }

            return aria.Contains("Half Price", StringComparison.OrdinalIgnoreCase) ||
                   aria.Contains("On special", StringComparison.OrdinalIgnoreCase) ||
                   aria.Contains("Save", StringComparison.OrdinalIgnoreCase) ||
                   aria.Contains("Find out more about", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractNameFromAria(string? aria)
        {
            if (string.IsNullOrWhiteSpace(aria))
            {
                return string.Empty;
            }

            if (aria.Contains("Find out more about", StringComparison.OrdinalIgnoreCase))
            {
                var start = aria.IndexOf("Find out more about", StringComparison.OrdinalIgnoreCase);
                if (start >= 0)
                {
                    var tail = aria.Substring(start + "Find out more about".Length).Trim();
                    var comma = tail.IndexOf(',', StringComparison.Ordinal);
                    return (comma > 0 ? tail[..comma] : tail).Trim();
                }
            }

            var saveMatch = Regex.Match(
                aria,
                @"Save\s+\$?\s*\d+(?:\.\d+)?\s*\.?\s*(?<name>[^,]+)",
                RegexOptions.IgnoreCase);
            if (saveMatch.Success)
            {
                return CleanName(saveMatch.Groups["name"].Value);
            }

            var commaIndex = aria.IndexOf(',', StringComparison.Ordinal);
            var head = commaIndex > 0 ? aria[..commaIndex] : aria;
            head = Regex.Replace(head, @"Half\s+Price\.?", "", RegexOptions.IgnoreCase);
            head = Regex.Replace(head, @"On\s+special\.?", "", RegexOptions.IgnoreCase);
            head = Regex.Replace(head, @"Save\s+\$?\s*\d+(?:\.\d+)?\.?", "", RegexOptions.IgnoreCase);
            return CleanName(head);
        }

        private static string CleanName(string value)
        {
            var cleaned = value.Trim();
            cleaned = cleaned.TrimStart('.', '-', ' ');
            cleaned = cleaned.TrimEnd('.', ' ');
            return cleaned;
        }

        private static bool IsValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (string.Equals(name, "out of stock", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!name.Any(char.IsLetter))
            {
                return false;
            }

            return name.Length >= 3;
        }
    }
}
