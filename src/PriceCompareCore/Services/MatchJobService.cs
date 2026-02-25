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
    // Runs batch match jobs and stores results into productmatch table.
    public class MatchJobService : IMatchJobService
    {
        private const decimal SameProductScore = 0.90m;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MatchJobService> _logger;

        public MatchJobService(IServiceScopeFactory scopeFactory, ILogger<MatchJobService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<Guid> StartAsync(MatchRunRequest request)
        {
            var jobId = request.ResumeJobId ?? Guid.NewGuid();

            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                MatchJob? job = null;

                if (request.ResumeJobId.HasValue)
                {
                    job = await db.MatchJobs.FirstOrDefaultAsync(j => j.Id == request.ResumeJobId.Value);
                    if (job == null)
                    {
                        throw new InvalidOperationException($"Resume job not found: {request.ResumeJobId}");
                    }

                    if (request.SourceShop != job.SourceShop || request.TargetShop != job.TargetShop)
                    {
                        throw new InvalidOperationException("Resume job source/target does not match.");
                    }

                    job.Status = "queued";
                    request.Mode = job.Mode;
                    request.Since = job.Since;
                    request.UseLlm = job.UseLlm;
                    request.Limit = job.LimitNum;
                    request.TopN = job.TopN;
                    job.ErrorMessage = null;
                    job.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    job = new MatchJob
                    {
                        Id = jobId,
                        SourceShop = request.SourceShop,
                        TargetShop = request.TargetShop,
                        Status = "queued",
                        Mode = string.IsNullOrWhiteSpace(request.Mode) ? "incremental" : request.Mode,
                        Since = request.Since,
                        UseLlm = request.UseLlm,
                        LimitNum = request.Limit,
                        TopN = request.TopN,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    db.MatchJobs.Add(job);
                }

                await db.SaveChangesAsync();
            }

            // Fire-and-forget background processing.
            _ = Task.Run(() => RunJobAsync(jobId, request));

            return jobId;
        }

        public async Task<MatchJob?> GetAsync(Guid jobId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            return await db.MatchJobs.AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId);
        }

        private async Task RunJobAsync(Guid jobId, MatchRunRequest request)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var matcher = scope.ServiceProvider.GetRequiredService<IProductMatchingService>();
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

                // Decide the time window for incremental mode.
                var mode = string.IsNullOrWhiteSpace(request.Mode) ? "incremental" : request.Mode.ToLowerInvariant();
                var since = request.Since;

                if (mode == "incremental" && since == null)
                {
                    // Default incremental window: last 7 days.
                    since = DateTime.UtcNow.AddDays(-7);
                }

                // Base query for source products.
                var baseQuery = db.Products.AsNoTracking()
                    .Where(p => p.ShopType == request.SourceShop);

                if (since.HasValue)
                {
                    baseQuery = baseQuery.Where(p => p.LastSeenAt >= since.Value);
                }

                // Total count (for progress display).
                var totalCount = await baseQuery.CountAsync();
                if (request.Limit > 0 && totalCount > request.Limit)
                {
                    totalCount = request.Limit;
                }

                job.Total = totalCount;
                job.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                IQueryable<Product> query = baseQuery
                    .OrderByDescending(p => p.LastSeenAt)
                    .ThenByDescending(p => p.ProductId);

                var remainingLimit = request.Limit;
                if (request.ResumeJobId.HasValue && job.Processed > 0)
                {
                    query = query.Skip(job.Processed);
                    if (remainingLimit > 0)
                    {
                        remainingLimit = Math.Max(remainingLimit - job.Processed, 0);
                    }
                }

                if (remainingLimit > 0)
                {
                    query = query.Take(remainingLimit);
                }
                else if (request.Limit > 0)
                {
                    job.Status = "completed";
                    job.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    return;
                }

                // Process in pages to avoid loading too many rows at once.
                const int pageSize = 200;
                var processed = 0;

                for (var page = 0; ; page++)
                {
                    var batch = await query
                        .Skip(page * pageSize)
                        .Take(pageSize)
                        .ToListAsync();

                    if (batch.Count == 0)
                    {
                        break;
                    }

                    foreach (var source in batch)
                    {
                        try
                        {
                            // Skip if we already have a fresh, high-confidence match.
                            if (!request.Force && await HasFreshMatchAsync(db, source))
                            {
                                job.Processed++;
                                processed++;
                                continue;
                            }

                            var candidates = await matcher.MatchCandidatesAsync(source.ProductId, request.TopN);
                            if (candidates.Count > 0)
                            {
                                var best = candidates.OrderByDescending(c => c.Score).First();

                                if (request.UseLlm && best.Score >= 0.50m && best.Score < SameProductScore)
                                {
                                    var decision = await llm.VerifyAsync(
                                        source,
                                        candidates.Select(c => c.Target).ToList());

                                    if (decision == "same_product" || decision == "comparable")
                                    {
                                        // Promote score so LLM-approved matches rank higher when reading cached results.
                                        best.Score = decision == "same_product" ? 0.95m : 0.70m;
                                        best.MatchType = decision;
                                    }
                                    else
                                    {
                                        // LLM rejected the match; skip writing.
                                        job.Processed++;
                                        processed++;
                                        continue;
                                    }
                                }

                                // Write topN candidates into productmatch.
                                ApplyCandidates(db, source, candidates);

                                // Update job counters.
                                var hasSame = candidates.Any(c => c.MatchType == "same_product" || c.Score >= SameProductScore);
                                var hasComparable = candidates.Any(c => c.MatchType == "comparable");

                                if (hasSame) job.Matched++;
                                else if (hasComparable) job.Comparable++;
                            }

                            job.Processed++;
                            processed++;

                            // Save in batches to reduce DB writes.
                            if (processed % 25 == 0)
                            {
                                job.UpdatedAt = DateTime.UtcNow;
                                await db.SaveChangesAsync();
                            }
                        }
                        catch (Exception exItem)
                        {
                            job.Failed++;
                            _logger.LogWarning(exItem, "Match job item failed source={SourceId}", source.ProductId);
                        }
                    }

                    // Save after each page.
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

                _logger.LogError(ex, "Match job failed jobId={JobId}", jobId);
            }
        }

        private static async Task<bool> HasFreshMatchAsync(AppDbContext db, Product source)
        {
            var match = await db.ProductMatches.AsNoTracking()
                .Where(m => m.SourceProductId == source.ProductId)
                .OrderByDescending(m => m.UpdatedAt)
                .FirstOrDefaultAsync();

            if (match == null)
            {
                return false;
            }

            // Fresh if updated after last scrape and score is high enough.
            return match.Score >= SameProductScore && match.UpdatedAt >= source.LastSeenAt;
        }

        private static void ApplyCandidates(AppDbContext db, Product source, IReadOnlyList<ProductMatchCandidate> candidates)
        {
            foreach (var c in candidates)
            {
                // Upsert by (SourceProductId, TargetProductId)
                var existing = db.ProductMatches
                    .FirstOrDefault(m => m.SourceProductId == source.ProductId && m.TargetProductId == c.Target.ProductId);

                if (existing == null)
                {
                    db.ProductMatches.Add(new ProductMatch
                    {
                        Id = Guid.NewGuid(),
                        SourceProductId = source.ProductId,
                        TargetProductId = c.Target.ProductId,
                        SourceShopType = source.ShopType ?? 0,
                        TargetShopType = c.Target.ShopType ?? 0,
                        Score = c.Score,
                        Method = c.Method,
                        MatchType = c.MatchType,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    existing.Score = c.Score;
                    existing.Method = c.Method;
                    existing.MatchType = c.MatchType;
                    existing.UpdatedAt = DateTime.UtcNow;
                }
            }
        }
    }
}
