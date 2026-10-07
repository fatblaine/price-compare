using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceCompareCore.Exceptions;
using PriceCompareCore.Interfaces;
using PriceCompareData.Common;
using PriceCompareData.Data;
using PriceCompareData.Entities;
using PriceCompareData.Entities.History;

namespace PriceCompareCore.Services
{
    /// <summary>
    /// Scrapes one Coles browse category (see <see cref="ColesCategories"/>). Replaces the 22 per-category
    /// copies; pages come from <see cref="IColesCategoryPageSource"/> (a real browser session in production).
    /// </summary>
    public class ColesCategoryScraperService : IColesCategoryScraperService
    {
        private const string BaseUrl = "https://www.coles.com.au";
        private const string ImageBase = "https://cdn.productimages.coles.com.au/productimages";

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly IColesCategoryPageSource _pageSource;
        private readonly ILogger<ColesCategoryScraperService> _logger;
        private readonly AppDbContext _dbContext;
        private readonly IIngestionService _ingestion;
        private readonly IScrapeExportService _export;

        public ColesCategoryScraperService(
            IColesCategoryPageSource pageSource,
            ILogger<ColesCategoryScraperService> logger,
            AppDbContext dbContext,
            IIngestionService ingestion,
            IScrapeExportService export)
        {
            _pageSource = pageSource;
            _logger = logger;
            _dbContext = dbContext;
            _ingestion = ingestion;
            _export = export;
        }

        public async Task<List<ColesDownProduct>> ScrapeAsync(ColesCategory category, int limit = 0, CancellationToken ct = default)
        {
            var hardCap = category.MaxItems;
            if (limit <= 0 || limit > hardCap) limit = hardCap;

            var all = new List<ColesDownProduct>();
            var seenIds = new HashSet<int>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var maxPages = category.MaxPages;
            var scrapedAt = DateTime.UtcNow;
            Exception? stoppedEarly = null;

            for (var page = 1; page <= maxPages && all.Count < hardCap; page++)
            {
                _logger.LogInformation("Coles {Slug}: fetching page {Page}", category.Slug, page);

                ParsedPage parsed;
                try
                {
                    var json = await _pageSource.GetPageJsonAsync(category, page, ct);
                    parsed = ParseProducts(json, scrapedAt, seenIds, seenNames);
                }
                catch (ColesScrapeException ex) when (all.Count > 0)
                {
                    // Keep what the earlier pages returned, but still fail the run below.
                    stoppedEarly = ex;
                    break;
                }

                if (parsed.Items.Count == 0)
                {
                    break;
                }

                all.AddRange(parsed.Items);

                // Stop on the last page instead of requesting an extra empty one.
                if (parsed.TotalPages is int totalPages && page >= totalPages)
                {
                    break;
                }
            }

            _logger.LogInformation("Coles {Slug}: extracted {Count} products.", category.Slug, all.Count);
            ColesDomFetchGuard.EnsureProducts(all.Count, $"{BaseUrl}/browse/{category.Slug}");

            await PersistAndExportAsync(category, all, scrapedAt, ct);

            if (stoppedEarly != null)
            {
                throw ColesDomFetchGuard.PartialFailure(all.Count, stoppedEarly);
            }

            return all.Take(limit).ToList();
        }

        private sealed record ParsedPage(List<ColesDownProduct> Items, int? TotalPages);

        private static ParsedPage ParseProducts(
            string json,
            DateTime scrapedAt,
            HashSet<int> seenIds,
            HashSet<string> seenNames)
        {
            var results = new List<ColesDownProduct>();
            ColesApiResponse? dto;
            try
            {
                dto = JsonSerializer.Deserialize<ColesApiResponse>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw ColesDomFetchGuard.ParseFailure(ex);
            }

            var searchResults = dto?.PageProps?.SearchResults;
            int? totalPages = searchResults is { NoOfResults: > 0, PageSize: > 0 }
                ? (int)Math.Ceiling(searchResults.NoOfResults.Value / (double)searchResults.PageSize.Value)
                : null;

            var items = searchResults?.Results;
            if (items == null || items.Count == 0)
            {
                return new ParsedPage(results, totalPages);
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
                    ProductUrl = BuildProductUrl(displayName, id),
                    WasPriceText = wasText ?? string.Empty,
                    IsSponsored = !string.IsNullOrWhiteSpace(item.AdId) || !string.IsNullOrWhiteSpace(item.AdSource),
                    ScrapedAt = scrapedAt
                });
            }

            return new ParsedPage(results, totalPages);
        }

        private async Task PersistAndExportAsync(
            ColesCategory category,
            IReadOnlyList<ColesDownProduct> products,
            DateTime scrapedAt,
            CancellationToken ct)
        {
            if (products.Count == 0)
            {
                return;
            }

            var offerType = category.OfferType;
            var today = scrapedAt.Date;
            var tomorrow = today.AddDays(1);
            var priceHistoryRows = new List<PriceHistory>(products.Count);

            var existingNames = new HashSet<string>(
                await _dbContext.PriceHistory
                    .Where(ph => ph.ShopType == ShopType.COLES && ph.OfferType == offerType && ph.ScrapedAt >= today && ph.ScrapedAt < tomorrow)
                    .Select(ph => ph.Name!)
                    .ToListAsync(ct),
                StringComparer.OrdinalIgnoreCase);

            foreach (var product in products)
            {
                var exists = existingNames.Contains(product.Name);

                var priceHistory = new PriceHistory
                {
                    Name = product.Name,
                    ImageUrl = product.ImageUrl ?? string.Empty,
                    CurrentPrice = product.CurrentPrice,
                    ScrapedAt = scrapedAt,
                    OfferType = offerType,
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
                    category.ExportSource,
                    scrapedAt,
                    priceHistoryRows,
                    productRows),
                ct);
        }

        private static string BuildDisplayName(ColesApiProduct item)
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

        private static string BuildProductUrl(string displayName, int id)
        {
            if (id <= 0)
            {
                return string.Empty;
            }

            var slug = Slugify(displayName);
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

        private class ColesApiResponse
        {
            public ColesPageProps? PageProps { get; set; }
        }

        private class ColesPageProps
        {
            public ColesSearchResults? SearchResults { get; set; }
        }

        private class ColesSearchResults
        {
            public int? NoOfResults { get; set; }
            public int? PageSize { get; set; }
            public List<ColesApiProduct> Results { get; set; } = new();
        }

        private class ColesApiProduct
        {
            public string? _type { get; set; }
            public int Id { get; set; }
            public string? AdId { get; set; }
            public string? AdSource { get; set; }
            public string? Name { get; set; }
            public string? Brand { get; set; }
            public string? Size { get; set; }
            public List<ColesImageUri>? ImageUris { get; set; }
            public ColesPricing? Pricing { get; set; }
        }

        private class ColesPricing
        {
            public decimal? Now { get; set; }
            public decimal? Was { get; set; }
            public string? PriceDescription { get; set; }
            public string? Comparable { get; set; }
        }

        private class ColesImageUri
        {
            public string? Uri { get; set; }
        }
    }
}
