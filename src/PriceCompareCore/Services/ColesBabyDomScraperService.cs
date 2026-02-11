using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using PriceCompareCore.Interfaces;
using PriceCompareData.Common;
using PriceCompareData.Data;
using PriceCompareData.Entities;
using PriceCompareData.Entities.Common;
using PriceCompareData.Entities.History;

namespace PriceCompareCore.Services
{
    public class ColesBabyDomScraperService : IColesBabyDomScraperService
    {
        private const string DefaultDataUrl = WebInfo.COLES_NEXT_DATA_BASE + "/en/browse/baby.json?slug=baby";
        private const string BaseUrl = "https://www.coles.com.au";
        private const string ImageBase = "https://cdn.productimages.coles.com.au/productimages";
        private const string DefaultUa =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

        private readonly HttpClient _httpClient;
        private readonly ILogger<ColesBabyDomScraperService> _logger;
        private readonly IDistributedCache _cache;
        private readonly AppDbContext _dbContext;
        private readonly IIngestionService _ingestion;
        private readonly IScrapeExportService _export;

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public ColesBabyDomScraperService(
            HttpClient httpClient,
            ILogger<ColesBabyDomScraperService> logger,
            IDistributedCache cache,
            AppDbContext dbContext,
            IIngestionService ingestion,
            IScrapeExportService export)
        {
            _httpClient = httpClient;
            _logger = logger;
            _cache = cache;
            _dbContext = dbContext;
            _ingestion = ingestion;
            _export = export;

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(GetUserAgent());
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-AU,en;q=0.9");
        }

        public async Task<List<ColesDownProduct>> ScrapeAsync(int limit = 0, CancellationToken ct = default)
        {
            var hardCap = GetMaxItems();
            if (limit <= 0) limit = hardCap;
            if (limit > hardCap) limit = hardCap;

            // var cached = await _cache.GetStringAsync(CacheKey.COLES_BABY_DOM_PRODUCTS, ct);
            // if (!string.IsNullOrWhiteSpace(cached))
            // {
            //     _logger.LogInformation("Coles JSON: returning cached baby products.");
            //     var cachedItems = JsonSerializer.Deserialize<List<ColesDownProduct>>(cached, JsonOptions) ?? new();
            //     return cachedItems.Take(limit).ToList();
            // }

            var all = new List<ColesDownProduct>();
            var seenIds = new HashSet<int>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var maxPages = GetMaxPages();
            var scrapedAt = DateTime.UtcNow;

            for (var page = 1; page <= maxPages && all.Count < hardCap; page++)
            {
                var url = BuildDataUrl(page);
                _logger.LogInformation("Coles JSON: fetching page {Page}: {Url}", page, url);

                var json = await FetchJsonAsync(url, ct);
                if (string.IsNullOrWhiteSpace(json))
                {
                    break;
                }

                var pageItems = ParseProducts(json, scrapedAt, seenIds, seenNames);
                if (pageItems.Count == 0)
                {
                    break;
                }

                all.AddRange(pageItems);
            }

            _logger.LogInformation("Coles JSON: extracted {Count} products.", all.Count);

            await _cache.SetStringAsync(
                CacheKey.COLES_BABY_DOM_PRODUCTS,
                JsonSerializer.Serialize(all, JsonOptions),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60)
                },
                ct);

            await PersistAndExportAsync(all, scrapedAt, ct);

            return all.Take(limit).ToList();
        }

        private async Task<string?> FetchJsonAsync(string url, CancellationToken ct)
        {
            try
            {
                using var resp = await _httpClient.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Coles JSON: non-OK status {Status} for {Url}", (int)resp.StatusCode, url);
                    return null;
                }

                return await resp.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Coles JSON: request failed for {Url}", url);
                return null;
            }
        }

        private static List<ColesDownProduct> ParseProducts(
            string json,
            DateTime scrapedAt,
            HashSet<int> seenIds,
            HashSet<string> seenNames)
        {
            var results = new List<ColesDownProduct>();
            ColesBabyApiResponse? dto;
            try
            {
                dto = JsonSerializer.Deserialize<ColesBabyApiResponse>(json, JsonOptions);
            }
            catch
            {
                return results;
            }

            var items = dto?.PageProps?.SearchResults?.Results;
            if (items == null || items.Count == 0)
            {
                return results;
            }

            foreach (var item in items)
            {
                if (!string.Equals(item._type, "PRODUCT", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var displayName = BuildDisplayName(item);
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                var price = item.Pricing?.Now;
                if (!price.HasValue || price.Value <= 0m)
                {
                    continue;
                }

                var id = item.Id;
                if (id > 0)
                {
                    if (!seenIds.Add(id))
                    {
                        continue;
                    }
                }
                else
                {
                    if (!seenNames.Add(displayName))
                    {
                        continue;
                    }
                }

                var was = item.Pricing?.Was;
                var wasText = item.Pricing?.PriceDescription;
                if (string.IsNullOrWhiteSpace(wasText) && was.HasValue)
                {
                    wasText = $"Was ${was.Value.ToString("0.00", CultureInfo.InvariantCulture)}";
                }

                var imageUri = item.ImageUris?.FirstOrDefault()?.Uri;

                results.Add(new ColesDownProduct
                {
                    Id = id,
                    Name = displayName,
                    CurrentPrice = price.Value,
                    OriginalPrice = was,
                    PricePerUnit = item.Pricing?.Comparable ?? string.Empty,
                    ImageUrl = BuildImageUrl(imageUri),
                    ProductUrl = BuildProductUrl(item, displayName, id),
                    WasPriceText = wasText ?? string.Empty,
                    IsSponsored = !string.IsNullOrWhiteSpace(item.AdId) || !string.IsNullOrWhiteSpace(item.AdSource),
                    ScrapedAt = scrapedAt
                });
            }

            return results;
        }

        private async Task PersistAndExportAsync(
            IReadOnlyList<ColesDownProduct> products,
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
                    ph.Name == product.Name &&
                    ph.ShopType == ShopType.COLES &&
                    ph.OfferType == OfferType.BABY &&
                    ph.ScrapedAt >= today &&
                    ph.ScrapedAt < today.AddDays(1));

                var priceHistory = new PriceHistory
                {
                    Name = product.Name,
                    ImageUrl = product.ImageUrl ?? string.Empty,
                    CurrentPrice = product.CurrentPrice,
                    ScrapedAt = scrapedAt,
                    OfferType = OfferType.BABY,
                    ShopType = ShopType.COLES
                };

                priceHistoryRows.Add(priceHistory);

                if (!exists)
                {
                    _dbContext.PriceHistory.Add(priceHistory);
                }
            }

            await _dbContext.SaveChangesAsync(ct);
            await _ingestion.UpsertColesDownAsync(products);
            var productRows = _ingestion.MapColesDownProducts(products);
            await _export.ExportAsync(
                new ScrapeExportRequest(
                    "coles_baby_json",
                    scrapedAt,
                    priceHistoryRows,
                    productRows),
                ct);
        }

        private static string BuildDataUrl(int page)
        {
            var baseUrl = GetDataUrl();
            if (baseUrl.Contains("{page}", StringComparison.OrdinalIgnoreCase))
            {
                return baseUrl.Replace("{page}", page.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
            }

            if (page <= 1)
            {
                return baseUrl;
            }

            var separator = baseUrl.Contains("?", StringComparison.Ordinal) ? "&" : "?";
            if (HasQueryParam(baseUrl, "page"))
            {
                return baseUrl;
            }

            return $"{baseUrl}{separator}page={page}";
        }

        private static bool HasQueryParam(string url, string name)
        {
            var pattern = $"[?&]{Regex.Escape(name)}=";
            return Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase);
        }

        private static string BuildDisplayName(ColesBabyApiProduct item)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(item.Brand))
            {
                parts.Add(item.Brand.Trim());
            }

            if (!string.IsNullOrWhiteSpace(item.Name))
            {
                parts.Add(item.Name.Trim());
            }

            var name = string.Join(" ", parts);
            if (!string.IsNullOrWhiteSpace(item.Size) &&
                !name.Contains(item.Size, StringComparison.OrdinalIgnoreCase))
            {
                name = string.IsNullOrWhiteSpace(name)
                    ? item.Size.Trim()
                    : $"{name} | {item.Size.Trim()}";
            }

            return NormalizeWhitespace(name);
        }

        private static string BuildProductUrl(ColesBabyApiProduct item, string displayName, int id)
        {
            if (id <= 0)
            {
                return string.Empty;
            }

            var slugSource = displayName;
            var slug = Slugify(slugSource);
            if (string.IsNullOrWhiteSpace(slug))
            {
                return $"{BaseUrl}/product/{id}";
            }

            return $"{BaseUrl}/product/{slug}-{id}";
        }

        private static string BuildImageUrl(string? uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                return string.Empty;
            }

            if (uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }

            if (uri.StartsWith("/", StringComparison.Ordinal))
            {
                return ImageBase + uri;
            }

            return ImageBase + "/" + uri;
        }

        private static string NormalizeWhitespace(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            return Regex.Replace(input, @"\s+", " ").Trim();
        }

        private static string Slugify(string input)
        {
            var lower = input.ToLowerInvariant();
            var cleaned = Regex.Replace(lower, "[^a-z0-9]+", "-");
            return cleaned.Trim('-');
        }

        private static int GetMaxPages()
        {
            var raw = Environment.GetEnvironmentVariable("COLES_BABY_MAX_PAGES");
            if (int.TryParse(raw, out var v) && v > 0)
            {
                return v;
            }

            raw = Environment.GetEnvironmentVariable("COLES_DOM_MAX_PAGES");
            return int.TryParse(raw, out v) && v > 0 ? v : 150;
        }

        private static int GetMaxItems()
        {
            var raw = Environment.GetEnvironmentVariable("COLES_BABY_MAX_ITEMS");
            if (int.TryParse(raw, out var v) && v > 0)
            {
                return v;
            }

            raw = Environment.GetEnvironmentVariable("COLES_DOM_MAX_ITEMS");
            return int.TryParse(raw, out v) && v > 0 ? v : 7000;
        }

        private static string GetDataUrl()
        {
            var raw = Environment.GetEnvironmentVariable("COLES_BABY_DATA_URL");
            return string.IsNullOrWhiteSpace(raw) ? DefaultDataUrl : raw.Trim();
        }

        private static string GetUserAgent()
        {
            var raw = Environment.GetEnvironmentVariable("COLES_USER_AGENT");
            return string.IsNullOrWhiteSpace(raw) ? DefaultUa : raw.Trim();
        }

        private class ColesBabyApiResponse
        {
            public ColesBabyPageProps? PageProps { get; set; }
        }

        private class ColesBabyPageProps
        {
            public ColesBabySearchResults? SearchResults { get; set; }
        }

        private class ColesBabySearchResults
        {
            public List<ColesBabyApiProduct> Results { get; set; } = new();
        }

        private class ColesBabyApiProduct
        {
            public string? _type { get; set; }
            public int Id { get; set; }
            public string? AdId { get; set; }
            public string? AdSource { get; set; }
            public bool Featured { get; set; }
            public string? Name { get; set; }
            public string? Brand { get; set; }
            public string? Description { get; set; }
            public string? Size { get; set; }
            public bool Availability { get; set; }
            public string? AvailabilityType { get; set; }
            public List<ColesBabyImageUri>? ImageUris { get; set; }
            public ColesBabyPricing? Pricing { get; set; }
        }

        private class ColesBabyPricing
        {
            public decimal? Now { get; set; }
            public decimal? Was { get; set; }
            public decimal? SaveAmount { get; set; }
            public string? PriceDescription { get; set; }
            public ColesBabyUnit? Unit { get; set; }
            public string? Comparable { get; set; }
            public string? PromotionType { get; set; }
            public bool? OnlineSpecial { get; set; }
        }

        private class ColesBabyUnit
        {
            public decimal? Quantity { get; set; }
            public decimal? OfMeasureQuantity { get; set; }
            public string? OfMeasureUnits { get; set; }
            public decimal? Price { get; set; }
            public string? OfMeasureType { get; set; }
            public bool? IsWeighted { get; set; }
            public bool? IsIncremental { get; set; }
        }

        private class ColesBabyImageUri
        {
            public string? AltText { get; set; }
            public string? Type { get; set; }
            public string? Uri { get; set; }
        }
    }
}
