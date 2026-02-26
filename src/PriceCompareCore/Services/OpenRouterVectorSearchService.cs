using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceCompareCore.Config;
using PriceCompareCore.Interfaces;
using PriceCompareData.Data;
using PriceCompareData.Entities.Compare;

namespace PriceCompareCore.Services
{
    // Vector search using OpenRouter embeddings (local cosine similarity).
    public class OpenRouterVectorSearchService : IVectorSearchService
    {
        private readonly AppDbContext _db;
        private readonly IEmbeddingService _embedding;
        private readonly OpenRouterOptions _options;
        private readonly ILogger<OpenRouterVectorSearchService> _logger;

        public OpenRouterVectorSearchService(
            AppDbContext db,
            IEmbeddingService embedding,
            IOptions<OpenRouterOptions> options,
            ILogger<OpenRouterVectorSearchService> logger)
        {
            _db = db;
            _embedding = embedding;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<IReadOnlyList<Product>> SearchAsync(Product source, int targetShop, int topN)
        {
            if (topN < 1) topN = 1;
            if (topN > 50) topN = 50;

            var sourceEmbedding = await _embedding.EmbedAsync(source);
            if (sourceEmbedding == null || sourceEmbedding.Length == 0)
            {
                return Array.Empty<Product>();
            }

            var basePool = Math.Clamp(_options.EmbeddingCandidatePool, 50, 1000);
            var pool = Math.Min(basePool, Math.Max(topN * 10, 50));

            var baseQuery = _db.Products.AsNoTracking()
                .Where(p => p.ShopType == targetShop && p.NormalizedName != null);

            var candidates = new List<Product>();

            // Token OR-ILIKE prefilter for candidate pool.
            if (!string.IsNullOrWhiteSpace(source.NormalizedName))
            {
                var tokens = source.NormalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t.Length >= 3)
                    .Distinct()
                    .ToList();

                if (tokens.Count > 0)
                {
                    IQueryable<Product>? tokenQuery = null;
                    foreach (var token in tokens)
                    {
                        var pattern = $"%{token}%";
                        var q = baseQuery.Where(p => EF.Functions.ILike(p.NormalizedName!, pattern));
                        tokenQuery = tokenQuery == null ? q : tokenQuery.Union(q);
                    }

                    if (tokenQuery != null)
                    {
                        candidates = await tokenQuery
                            .OrderByDescending(p => p.LastSeenAt)
                            .Take(pool)
                            .ToListAsync();
                    }
                }
            }

            // Fill with recent items if token pool is empty or small.
            if (candidates.Count < pool)
            {
                var remaining = pool - candidates.Count;
                var existingIds = candidates.Select(p => p.ProductId).ToList();
                var fill = await baseQuery
                    .Where(p => !existingIds.Contains(p.ProductId))
                    .OrderByDescending(p => p.LastSeenAt)
                    .Take(remaining)
                    .ToListAsync();

                candidates.AddRange(fill);
            }

            if (candidates.Count == 0)
            {
                return Array.Empty<Product>();
            }

            _logger.LogInformation("Vector candidate pool size={Pool} targetShop={TargetShop}", candidates.Count, targetShop);

            var scored = new List<(Product Product, float Score)>(candidates.Count);
            var embeddedCount = 0;

            foreach (var candidate in candidates)
            {
                var candEmbed = await _embedding.EmbedAsync(candidate);
                if (candEmbed == null || candEmbed.Length == 0)
                {
                    continue;
                }

                var sim = CosineSimilarity(sourceEmbedding, candEmbed);
                scored.Add((candidate, sim));
                embeddedCount++;
            }

            if (scored.Count == 0)
            {
                return Array.Empty<Product>();
            }

            _logger.LogInformation("Vector search scored={Count} embedded={Embedded}", scored.Count, embeddedCount);

            return scored
                .OrderByDescending(s => s.Score)
                .Take(topN)
                .Select(s => s.Product)
                .ToList();
        }

        private static float CosineSimilarity(float[] a, float[] b)
        {
            var len = Math.Min(a.Length, b.Length);
            if (len == 0)
            {
                return 0f;
            }

            double dot = 0;
            double normA = 0;
            double normB = 0;

            for (var i = 0; i < len; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
            return denom <= 0 ? 0f : (float)(dot / denom);
        }
    }
}
