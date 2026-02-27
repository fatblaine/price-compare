using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PriceCompareCore.Interfaces;
using PriceCompareData.Data;
using PriceCompareData.DTOs;
using PriceCompareData.Entities.Compare;

namespace PriceCompareCore.Services
{
    // Runs LLM review over existing productmatch rows in score range [0.5, 0.9).
    public class MatchLlmReviewService : IMatchLlmReviewService
    {
        private const decimal MinScore = 0.50m;
        private const decimal MaxScore = 0.90m;
        private const decimal SameProductScore = 0.95m;
        private const int PageSize = 200;
        private const string ManualMethod = "manual";
        private const string LlmMethod = "llm";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MatchLlmReviewService> _logger;

        public MatchLlmReviewService(IServiceScopeFactory scopeFactory, ILogger<MatchLlmReviewService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<Guid> StartAsync(MatchLlmReviewRequest request)
        {
            var jobId = Guid.NewGuid();

            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var job = new MatchJob
                {
                    Id = jobId,
                    SourceShop = request.SourceShop,
                    TargetShop = request.TargetShop,
                    Status = "queued",
                    Mode = "llm_review",
                    UseLlm = true,
                    LimitNum = request.Limit,
                    TopN = request.TopN,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                db.MatchJobs.Add(job);
                await db.SaveChangesAsync();
            }

            // Fire-and-forget background processing.
            _ = Task.Run(() => RunJobAsync(jobId, request));

            return jobId;
        }

        private async Task RunJobAsync(Guid jobId, MatchLlmReviewRequest request)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var llm = scope.ServiceProvider.GetRequiredService<IMatchVerificationService>();

            var job = await db.MatchJobs.FirstOrDefaultAsync(j => j.Id == jobId);
            if (job == null)
            {
                return;
            }

            try
            {
                job.Status = "running";
                job.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                var topN = Math.Clamp(request.TopN, 1, 50);
                var limit = request.Limit < 0 ? 0 : request.Limit;

                var baseMatches = db.ProductMatches.AsNoTracking()
                    .Where(m => m.SourceShopType == request.SourceShop && m.TargetShopType == request.TargetShop)
                    .Where(m => m.Method == null || m.Method != ManualMethod);

                if (!request.Force)
                {
                    var reviewedSources = db.ProductMatches.AsNoTracking()
                        .Where(m => m.SourceShopType == request.SourceShop && m.TargetShopType == request.TargetShop)
                        .Where(m => m.Method == LlmMethod)
                        .Select(m => m.SourceProductId)
                        .Distinct();

                    baseMatches = baseMatches.Where(m => !reviewedSources.Contains(m.SourceProductId));
                }

                // Best score per source (excluding manual + llm_review).
                var bestBySource = baseMatches
                    .GroupBy(m => m.SourceProductId)
                    .Select(g => new { SourceProductId = g.Key, BestScore = g.Max(m => m.Score) })
                    .Where(x => x.BestScore >= MinScore && x.BestScore < MaxScore);

                var totalCount = await bestBySource.CountAsync();
                if (limit > 0 && totalCount > limit)
                {
                    totalCount = limit;
                }

                job.Total = totalCount;
                job.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                IQueryable<Guid> sourceIds = bestBySource
                    .OrderBy(x => x.SourceProductId)
                    .Select(x => x.SourceProductId);

                if (limit > 0)
                {
                    sourceIds = sourceIds.Take(limit);
                }

                var processed = 0;

                for (var page = 0; ; page++)
                {
                    var batch = await sourceIds
                        .Skip(page * PageSize)
                        .Take(PageSize)
                        .ToListAsync();

                    if (batch.Count == 0)
                    {
                        break;
                    }

                    foreach (var sourceId in batch)
                    {
                        try
                        {
                            var source = await db.Products.AsNoTracking()
                                .FirstOrDefaultAsync(p => p.ProductId == sourceId);

                            if (source == null)
                            {
                                job.Processed++;
                                processed++;
                                continue;
                            }

                            var matchRowsQuery = db.ProductMatches.AsNoTracking()
                                .Where(m => m.SourceProductId == sourceId)
                                .Where(m => m.SourceShopType == request.SourceShop && m.TargetShopType == request.TargetShop)
                                .Where(m => m.Method == null || m.Method != ManualMethod)
                                .Where(m => m.Score >= MinScore && m.Score < MaxScore)
                                .OrderByDescending(m => m.Score)
                                .ThenByDescending(m => m.UpdatedAt)
                                .Take(topN);

                            if (!request.Force)
                            {
                                matchRowsQuery = matchRowsQuery.Where(m => m.Method == null || m.Method != LlmMethod);
                            }

                            var matchRows = await matchRowsQuery.ToListAsync();

                            if (matchRows.Count == 0)
                            {
                                job.Processed++;
                                processed++;
                                continue;
                            }

                            var targetIds = matchRows.Select(m => m.TargetProductId).Distinct().ToList();
                            var targets = await db.Products.AsNoTracking()
                                .Where(p => targetIds.Contains(p.ProductId))
                                .ToListAsync();

                            var targetMap = targets.ToDictionary(p => p.ProductId, p => p);
                            var candidates = matchRows
                                .Select(m => targetMap.TryGetValue(m.TargetProductId, out var t) ? t : null)
                                .Where(t => t != null)
                                .Cast<Product>()
                                .ToList();

                            if (candidates.Count == 0)
                            {
                                job.Processed++;
                                processed++;
                                continue;
                            }

                            var decision = await llm.VerifyAsync(source, candidates);

                            var bestMatchId = matchRows[0].Id;
                            var best = await db.ProductMatches.FirstOrDefaultAsync(m => m.Id == bestMatchId);

                            if (best != null && best.Method != ManualMethod)
                            {
                                if (decision == "same_product")
                                {
                                    best.Score = SameProductScore;
                                    best.MatchType = "same_product";
                                    job.Matched++;
                                }
                                else if (decision == "comparable")
                                {
                                    best.MatchType = "comparable";
                                    job.Comparable++;
                                }

                                // Mark reviewed even when decision is no/uncertain.
                                best.Method = LlmMethod;
                                best.UpdatedAt = DateTime.UtcNow;
                            }

                            job.Processed++;
                            processed++;

                            if (processed % 25 == 0)
                            {
                                job.UpdatedAt = DateTime.UtcNow;
                                await db.SaveChangesAsync();
                            }
                        }
                        catch (Exception exItem)
                        {
                            job.Failed++;
                            _logger.LogWarning(exItem, "LLM review item failed source={SourceId}", sourceId);
                        }
                    }

                    job.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }

                job.Status = "completed";
                job.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                job.Status = "failed";
                job.ErrorMessage = ex.Message;
                job.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                _logger.LogError(ex, "LLM review job failed jobId={JobId}", jobId);
            }
        }
    }
}
