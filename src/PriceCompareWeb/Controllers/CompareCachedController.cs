using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceCompareData.Common;
using PriceCompareData.Data;
using PriceCompareData.DTOs;
using PriceCompareData.Entities.Compare;
using PriceCompareWeb.Controllers.Models;

namespace PriceCompareWeb.Controllers
{
    [ApiController]
    [Route("api/compare-cached")]
    public class CompareCachedController : ControllerBase
    {
        private const int MaxBatchSize = 50;
        private const string MatchTypeSameProduct = "same_product";

        private readonly AppDbContext _dbContext;

        public CompareCachedController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Read-only compare (cached). Returns precomputed matches by keyword.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> CompareCached([FromQuery] string keyword, [FromQuery] int sourceShop, [FromQuery] int topN = 10)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return BadRequest("Keyword cannot be empty.");
            }

            if (topN < 1) topN = 1;
            if (topN > 20) topN = 20;

            var priceCache = new Dictionary<(string name, int shopType), decimal?>();
            async Task<decimal?> GetLatestPriceCachedAsync(string name, int shopType)
            {
                var key = (name, shopType);
                if (priceCache.TryGetValue(key, out var cached))
                {
                    return cached;
                }

                var price = await GetLatestPriceAsync(name, shopType);
                priceCache[key] = price;
                return price;
            }

            // Fetch products from source shop
            var sourceList = await _dbContext.Products
                .AsNoTracking()
                .Where(p => p.ShopType == sourceShop && p.Name.Contains(keyword))
                .OrderBy(p => p.Name)
                .Take(20)
                .ToListAsync();

            var results = new List<object>();

            foreach (var s in sourceList)
            {
                var sourceShopType = s.ShopType ?? sourceShop;
                var sourcePrice = await GetLatestPriceCachedAsync(s.Name, sourceShopType);
                var matches = await _dbContext.ProductMatches
                    .AsNoTracking()
                    .Where(m => m.SourceProductId == s.ProductId)
                    .OrderByDescending(m => m.Score)
                    .ThenByDescending(m => m.UpdatedAt)
                    .Take(topN)
                    .ToListAsync();
                var reversed = false;

                if (matches.Count == 0)
                {
                    matches = await _dbContext.ProductMatches
                        .AsNoTracking()
                        .Where(m => m.TargetProductId == s.ProductId)
                        .OrderByDescending(m => m.Score)
                        .ThenByDescending(m => m.UpdatedAt)
                        .Take(topN)
                        .ToListAsync();
                    reversed = true;
                }

                var targets = new List<object>();

                foreach (var m in matches)
                {
                    var targetId = reversed ? m.SourceProductId : m.TargetProductId;
                    var t = await _dbContext.Products.AsNoTracking()
                        .FirstOrDefaultAsync(p => p.ProductId == targetId);

                    if (t == null)
                    {
                        continue;
                    }

                    decimal? targetPrice = null;
                    if (t.ShopType.HasValue)
                    {
                        targetPrice = await GetLatestPriceCachedAsync(t.Name, t.ShopType.Value);
                    }

                    targets.Add(new
                    {
                        t.ProductId,
                        t.Name,
                        t.Brand,
                        t.SizeValue,
                        t.SizeUnit,
                        ShopType = t.ShopType,
                        matchScore = m.Score,
                        matchMethod = m.Method,
                        matchType = m.MatchType,
                        price = targetPrice,
                        pricePerUnit = GetPricePerUnit(targetPrice, t.SizeValue)
                    });
                }

                results.Add(new
                {
                    source = new
                    {
                        s.ProductId,
                        s.Name,
                        s.Brand,
                        s.SizeValue,
                        s.SizeUnit,
                        ShopType = s.ShopType,
                        price = sourcePrice,
                        pricePerUnit = GetPricePerUnit(sourcePrice, s.SizeValue)
                    },
                    targets
                });
            }

            return Ok(new { matches = results });
        }

        /// <summary>
        /// Read-only compare (cached). Returns precomputed matches by sourceProductId.
        /// </summary>
        [HttpGet("by-product")]
        public async Task<IActionResult> CompareCachedByProduct([FromQuery] Guid sourceProductId, [FromQuery] int topN = 10)
        {
            if (sourceProductId == Guid.Empty)
            {
                return BadRequest("sourceProductId is required.");
            }

            if (topN < 1) topN = 1;
            if (topN > 20) topN = 20;

            var matches = await _dbContext.ProductMatches
                .AsNoTracking()
                .Where(m => m.SourceProductId == sourceProductId)
                .OrderByDescending(m => m.Score)
                .ThenByDescending(m => m.UpdatedAt)
                .Take(topN)
                .ToListAsync();
            var reversed = false;

            if (matches.Count == 0)
            {
                matches = await _dbContext.ProductMatches
                    .AsNoTracking()
                    .Where(m => m.TargetProductId == sourceProductId)
                    .OrderByDescending(m => m.Score)
                    .ThenByDescending(m => m.UpdatedAt)
                    .Take(topN)
                    .ToListAsync();
                reversed = true;
            }

            if (matches.Count == 0)
            {
                return Ok(new ProductMatchListResult
                {
                    Found = false,
                    Reason = "not_precomputed",
                    Matches = new List<ProductMatchCandidate>()
                });
            }

            // Batch-fetch all target products in one query
            var targetIds = matches
                .Select(m => reversed ? m.SourceProductId : m.TargetProductId)
                .ToList();

            var productMap = await _dbContext.Products
                .AsNoTracking()
                .Where(p => targetIds.Contains(p.ProductId))
                .ToDictionaryAsync(p => p.ProductId);

            // Batch-fetch latest prices in one GROUP BY query
            var nameList = productMap.Values
                .Where(p => p.ShopType.HasValue)
                .Select(p => p.Name)
                .Distinct()
                .ToList();

            var shopList = productMap.Values
                .Where(p => p.ShopType.HasValue)
                .Select(p => p.ShopType!.Value)
                .Distinct()
                .ToList();

            var priceMap = new Dictionary<(string, int), decimal?>();
            if (nameList.Count > 0)
            {
                var priceRows = await _dbContext.PriceHistory
                    .AsNoTracking()
                    .Where(ph => nameList.Contains(ph.Name) && ph.ShopType.HasValue && shopList.Contains(ph.ShopType!.Value))
                    .GroupBy(ph => new { ph.Name, ph.ShopType })
                    .Select(g => new
                    {
                        g.Key.Name,
                        ShopType = (int)g.Key.ShopType!,
                        Price = (decimal?)g.OrderByDescending(x => x.ScrapedAt).First().CurrentPrice
                    })
                    .ToListAsync();

                foreach (var row in priceRows)
                    priceMap[(row.Name, row.ShopType)] = row.Price;
            }

            var results = new List<ProductMatchCandidate>();

            foreach (var m in matches)
            {
                var targetId = reversed ? m.SourceProductId : m.TargetProductId;
                if (!productMap.TryGetValue(targetId, out var target))
                    continue;

                decimal? targetPrice = null;
                if (target.ShopType.HasValue)
                    priceMap.TryGetValue((target.Name, target.ShopType.Value), out targetPrice);

                results.Add(new ProductMatchCandidate
                {
                    Target = target,
                    Score = m.Score,
                    Method = m.Method ?? string.Empty,
                    MatchType = m.MatchType,
                    LatestPrice = targetPrice,
                    PricePerUnit = GetPricePerUnit(targetPrice, target.SizeValue)
                });
            }

            return Ok(new ProductMatchListResult
            {
                Found = results.Count > 0,
                Reason = results.Count > 0 ? null : "target_not_found",
                Matches = results
            });
        }

        /// <summary>
        /// Read-only compare (cached), batched. Returns the best same_product candidate per source
        /// product — one request and a fixed four queries for a whole page of product cards,
        /// instead of one request (and up to four queries) per product.
        /// Selection matches by-product exactly: forward matches first, reverse only when a product
        /// has no forward match at all, ordered by score then updatedAt, then the first
        /// same_product candidate that has a price.
        /// </summary>
        [HttpPost("by-products")]
        public async Task<IActionResult> CompareCachedByProducts([FromBody] BatchCompareRequest request)
        {
            var sourceIds = (request?.SourceProductIds ?? new List<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (sourceIds.Count == 0)
            {
                return BadRequest("sourceProductIds is required.");
            }

            if (sourceIds.Count > MaxBatchSize)
            {
                return BadRequest($"sourceProductIds must contain at most {MaxBatchSize} ids.");
            }

            var topN = NormaliseTopN(request!.TopN);
            var candidatesBySource = await LoadCandidatesAsync(sourceIds, topN);

            var results = new Dictionary<string, ProductMatchCandidate?>(sourceIds.Count);
            foreach (var sourceId in sourceIds)
            {
                candidatesBySource.TryGetValue(sourceId, out var candidates);
                results[sourceId.ToString()] = candidates?
                    .FirstOrDefault(c => c.MatchType == MatchTypeSameProduct && c.LatestPrice != null);
            }

            return Ok(new { results });
        }

        private static int NormaliseTopN(int topN)
        {
            if (topN < 1) return 1;
            if (topN > 20) return 20;
            return topN;
        }

        /// <summary>
        /// Loads precomputed match candidates for several source products using a fixed number of
        /// queries: forward matches, reverse matches (only for sources with no forward match),
        /// target products, and latest prices.
        /// </summary>
        private async Task<Dictionary<Guid, List<ProductMatchCandidate>>> LoadCandidatesAsync(
            IReadOnlyCollection<Guid> sourceIds, int topN)
        {
            // (sourceProductId, match, reversed) — reversed means the source product is the match target.
            var matchesBySource = new Dictionary<Guid, List<(ProductMatch Match, bool Reversed)>>();

            var forward = await _dbContext.ProductMatches
                .AsNoTracking()
                .Where(m => sourceIds.Contains(m.SourceProductId))
                .ToListAsync();

            foreach (var group in forward.GroupBy(m => m.SourceProductId))
            {
                matchesBySource[group.Key] = OrderAndTake(group, topN)
                    .Select(m => (m, false))
                    .ToList();
            }

            var missing = sourceIds.Where(id => !matchesBySource.ContainsKey(id)).ToList();
            if (missing.Count > 0)
            {
                var reverse = await _dbContext.ProductMatches
                    .AsNoTracking()
                    .Where(m => missing.Contains(m.TargetProductId))
                    .ToListAsync();

                foreach (var group in reverse.GroupBy(m => m.TargetProductId))
                {
                    matchesBySource[group.Key] = OrderAndTake(group, topN)
                        .Select(m => (m, true))
                        .ToList();
                }
            }

            var targetIds = matchesBySource.Values
                .SelectMany(list => list.Select(x => x.Reversed ? x.Match.SourceProductId : x.Match.TargetProductId))
                .Distinct()
                .ToList();

            if (targetIds.Count == 0)
            {
                return new Dictionary<Guid, List<ProductMatchCandidate>>();
            }

            var productMap = await _dbContext.Products
                .AsNoTracking()
                .Where(p => targetIds.Contains(p.ProductId))
                .ToDictionaryAsync(p => p.ProductId);

            var priceMap = await LoadLatestPricesAsync(productMap.Values);

            var output = new Dictionary<Guid, List<ProductMatchCandidate>>(matchesBySource.Count);
            foreach (var (sourceId, matches) in matchesBySource)
            {
                var candidates = new List<ProductMatchCandidate>(matches.Count);
                foreach (var (match, reversed) in matches)
                {
                    var targetId = reversed ? match.SourceProductId : match.TargetProductId;
                    if (!productMap.TryGetValue(targetId, out var target))
                    {
                        continue;
                    }

                    decimal? targetPrice = null;
                    if (target.ShopType.HasValue)
                    {
                        priceMap.TryGetValue((target.Name, target.ShopType.Value), out targetPrice);
                    }

                    candidates.Add(new ProductMatchCandidate
                    {
                        Target = target,
                        Score = match.Score,
                        Method = match.Method ?? string.Empty,
                        MatchType = match.MatchType,
                        LatestPrice = targetPrice,
                        PricePerUnit = GetPricePerUnit(targetPrice, target.SizeValue)
                    });
                }

                output[sourceId] = candidates;
            }

            return output;
        }

        private static IEnumerable<ProductMatch> OrderAndTake(IEnumerable<ProductMatch> matches, int topN) =>
            matches
                .OrderByDescending(m => m.Score)
                .ThenByDescending(m => m.UpdatedAt)
                .Take(topN);

        /// <summary>
        /// Latest price per (name, shopType) for the given products, in one GROUP BY query.
        /// </summary>
        private async Task<Dictionary<(string, int), decimal?>> LoadLatestPricesAsync(IEnumerable<Product> products)
        {
            var priceMap = new Dictionary<(string, int), decimal?>();

            var withShop = products.Where(p => p.ShopType.HasValue).ToList();
            var nameList = withShop.Select(p => p.Name).Distinct().ToList();
            var shopList = withShop.Select(p => p.ShopType!.Value).Distinct().ToList();

            if (nameList.Count == 0)
            {
                return priceMap;
            }

            var priceRows = await _dbContext.PriceHistory
                .AsNoTracking()
                .Where(ph => nameList.Contains(ph.Name) && ph.ShopType.HasValue && shopList.Contains(ph.ShopType!.Value))
                .GroupBy(ph => new { ph.Name, ph.ShopType })
                .Select(g => new
                {
                    g.Key.Name,
                    ShopType = (int)g.Key.ShopType!,
                    Price = (decimal?)g.OrderByDescending(x => x.ScrapedAt).First().CurrentPrice
                })
                .ToListAsync();

            foreach (var row in priceRows)
            {
                priceMap[(row.Name, row.ShopType)] = row.Price;
            }

            return priceMap;
        }

        private async Task<decimal?> GetLatestPriceAsync(string name, int shopType)
        {
            return await _dbContext.PriceHistory
                .AsNoTracking()
                .Where(ph => ph.ShopType == shopType && ph.Name == name)
                .OrderByDescending(ph => ph.ScrapedAt)
                .Select(ph => (decimal?)ph.CurrentPrice)
                .FirstOrDefaultAsync();
        }

        private static decimal? GetPricePerUnit(decimal? price, decimal? sizeValue)
        {
            if (!price.HasValue || !sizeValue.HasValue || sizeValue.Value <= 0m)
            {
                return null;
            }

            return price.Value / sizeValue.Value;
        }
    }
}
