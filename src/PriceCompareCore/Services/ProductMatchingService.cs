using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceCompareCore.Interfaces;
using PriceCompareData.Common;
using PriceCompareData.Data;
using PriceCompareData.DTOs;
using PriceCompareData.Entities.Compare;

namespace PriceCompareCore.Services
{
    public class ProductMatchingService : IProductMatchingService
    {
        private const decimal SameProductScore = 0.90m;
        private const decimal ComparableScore = 0.50m;

        private readonly AppDbContext _db;
        private readonly IVectorSearchService _vector;
        private readonly ILogger<ProductMatchingService> _logger;

        public ProductMatchingService(
                    AppDbContext db,
                    IVectorSearchService vector,
                    ILogger<ProductMatchingService> logger)
        {
            _db = db;
            _vector = vector;
            _logger = logger;
        }

        public async Task<IReadOnlyList<ProductMatchCandidate>> MatchCandidatesAsync(Guid sourceProductId, int topN)
        {
            if (topN < 1) topN = 1;
            if (topN > 50) topN = 50;

            var source = await _db.Products.AsNoTracking()
                            .FirstOrDefaultAsync(p => p.ProductId == sourceProductId);

            if (source == null)
            {
                return Array.Empty<ProductMatchCandidate>();
            }

            var targetShop = source.ShopType == ShopType.COLES ? ShopType.WOOLWORTHS : ShopType.COLES;

            // Step A: exact match by normalized name (+ size if available)
            var candidates = await FindExactCandidatesAsync(source, targetShop);
            var method = "exact";

            // Step B: vector candidates (optional)
            if (candidates.Count == 0)
            {
                candidates = await FindVectorCandidatesAsync(source, targetShop, topN);
                method = "vector";
            }

            // Step C: fallback token search
            if (candidates.Count == 0)
            {
                candidates = await FindTokenCandidatesAsync(source, targetShop);
                method = "fallback";
            }

            if (candidates.Count == 0)
            {
                return Array.Empty<ProductMatchCandidate>();
            }

            var scored = new List<ProductMatchCandidate>();

            for (int i = 0; i < candidates.Count; i++)
            {
                var target = candidates[i];
                var score = ComputeScore(source, target, method, i, candidates.Count);

                var matchType = score >= SameProductScore
                    ? "same_product"
                    : (score >= ComparableScore && IsComparable(source, target) ? "comparable" : null);

                scored.Add(new ProductMatchCandidate
                {
                    Target = target,
                    Score = score,
                    Method = method,
                    MatchType = matchType
                });
            }

            return scored
                .OrderByDescending(x => x.Score)
                .Take(topN)
                .ToList();
        }

        // Exact match by normalized name (+ size if available)
        private async Task<List<Product>> FindExactCandidatesAsync(Product source, int targetShop)
        {
            var sourceName = GetMatchName(source);
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                return new List<Product>();
            }

            var query = _db.Products.AsNoTracking()
                .Where(p => p.ShopType == targetShop);

            if (!string.IsNullOrWhiteSpace(source.NormalizedName))
            {
                query = query.Where(p => p.NormalizedName == source.NormalizedName);
            }
            else
            {
                query = query.Where(p => p.Name == source.Name);
            }

            // Optional size constraint if size is known
            if (HasStandardSize(source))
            {
                var low = source.SizeStandardValue!.Value * 0.9m;
                var high = source.SizeStandardValue!.Value * 1.1m;
                query = query.Where(p =>
                    p.SizeStandardUnit == source.SizeStandardUnit &&
                    p.SizeStandardValue >= low &&
                    p.SizeStandardValue <= high);
            }
            else if (source.SizeValue.HasValue && !string.IsNullOrWhiteSpace(source.SizeUnit))
            {
                var low = source.SizeValue.Value * 0.9m;
                var high = source.SizeValue.Value * 1.1m;
                query = query.Where(p =>
                    p.SizeUnit == source.SizeUnit &&
                    p.SizeValue >= low &&
                    p.SizeValue <= high);
            }

            return await query.Take(50).ToListAsync();
        }

        // Vector search candidates (can return empty list when not configured)
        private async Task<List<Product>> FindVectorCandidatesAsync(Product source, int targetShop, int topN)
        {
            // Vector search can return empty list when not configured.
            var vectorCandidates = await _vector.SearchAsync(source, targetShop, Math.Max(topN, 20));
            return vectorCandidates.ToList();
        }

        // Fallback token search (not implemented yet, can return empty list)
        private async Task<List<Product>> FindTokenCandidatesAsync(Product source, int targetShop)
        {
            var sourceName = GetMatchName(source);
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                return new List<Product>();
            }

            var tokens = sourceName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 3)
                .Distinct()
                .ToList();

            if (tokens.Count == 0)
            {
                return new List<Product>();
            }

            var baseQuery = _db.Products.AsNoTracking()
                .Where(p => p.ShopType == targetShop && p.NormalizedName != null);

            // Build OR query with Union to avoid AND over-restricting.
            IQueryable<Product>? tokenQuery = null;
            foreach (var token in tokens)
            {
                var pattern = $"%{token}%";
                var q = baseQuery.Where(p => EF.Functions.ILike(p.NormalizedName!, pattern));
                tokenQuery = tokenQuery == null ? q : tokenQuery.Union(q);
            }

            if (tokenQuery == null)
            {
                return new List<Product>();
            }

            return await tokenQuery
                .OrderByDescending(p => p.LastSeenAt)
                .Take(80)
                .ToListAsync();
        }

        private static decimal ComputeScore(Product source, Product target, string method, int index, int total)
        {
            var tokenScore = TokenSimilarity(GetMatchName(source), GetMatchName(target));
            var categoryScore = (source.CategoryId.HasValue && source.CategoryId == target.CategoryId) ? 1.0m : 0m;
            var sizePenalty = SizePenalty(source, target);

            // Rank bonus: earlier candidates are slightly favored.
            var rankScore = total > 0 ? 1.0m - ((decimal)index / total) : 0m;

            if (method == "vector")
            {
                return Math.Clamp((0.55m * rankScore) + (0.35m * tokenScore) + (0.10m * categoryScore) - sizePenalty, 0m, 1m);
            }

            return Math.Clamp((0.80m * tokenScore) + (0.20m * categoryScore) - sizePenalty, 0m, 1m);
        }

        private static bool IsComparable(Product source, Product target)
        {
            // Require category match if both exist.
            if (source.CategoryId.HasValue && target.CategoryId.HasValue &&
                source.CategoryId.Value != target.CategoryId.Value)
            {
                return false;
            }

            // If size is unknown on either side, allow comparable.
            if (source.SizeUnknown || target.SizeUnknown)
            {
                return true;
            }

            if (!string.Equals(source.SizeStandardUnit, target.SizeStandardUnit, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (source.SizeStandardValue == null || target.SizeStandardValue == null)
            {
                return true;
            }

            var diff = Math.Abs(source.SizeStandardValue.Value - target.SizeStandardValue.Value);
            var baseVal = source.SizeStandardValue.Value == 0m ? 1m : source.SizeStandardValue.Value;
            var ratio = diff / baseVal;
            return ratio <= 0.4m;
        }

        private static decimal SizePenalty(Product source, Product target)
        {
            if (source.SizeUnknown || target.SizeUnknown)
            {
                return 0m;
            }

            if (!string.Equals(source.SizeStandardUnit, target.SizeStandardUnit, StringComparison.OrdinalIgnoreCase))
            {
                return 0.20m;
            }

            if (source.SizeStandardValue == null || target.SizeStandardValue == null)
            {
                return 0m;
            }

            var diff = Math.Abs(source.SizeStandardValue.Value - target.SizeStandardValue.Value);
            var baseVal = source.SizeStandardValue.Value == 0m ? 1m : source.SizeStandardValue.Value;
            var ratio = diff / baseVal;
            return ratio > 0.2m ? 0.20m : ratio;
        }

        private static bool HasStandardSize(Product p)
        {
            return !p.SizeUnknown &&
                   p.SizeStandardValue.HasValue &&
                   !string.IsNullOrWhiteSpace(p.SizeStandardUnit);
        }

        private static string? GetMatchName(Product p)
        {
            return !string.IsNullOrWhiteSpace(p.NormalizedName) ? p.NormalizedName : p.Name;
        }

        private static decimal TokenSimilarity(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return 0m;
            }

            var setA = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var setB = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

            var inter = setA.Intersect(setB).Count();
            var union = setA.Union(setB).Count();

            return union == 0 ? 0m : (decimal)inter / union;
        }
    }
}