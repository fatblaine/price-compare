using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PriceCompareCore.Interfaces;
using PriceCompareCore.Services;
using Quartz;

namespace PriceCompareCore.Jobs
{
    /// <summary>
    /// Scrapes one Coles category; the slug comes from the job's data map, so every category in
    /// <see cref="ColesCategories"/> shares this class under its own (unchanged) job name.
    /// A failed run schedules one retry a few hours later, after any Imperva block has cooled down.
    /// </summary>
    public class ColesCategoryDomJob : IJob
    {
        public const string SlugKey = "slug";
        public const string AttemptKey = "attempt";

        private const int DefaultMaxAttempts = 2;
        private const int DefaultRetryDelayMinutes = 200;

        private readonly IColesCategoryScraperService _scraperService;
        private readonly ILogger<ColesCategoryDomJob> _logger;

        public ColesCategoryDomJob(IColesCategoryScraperService scraperService, ILogger<ColesCategoryDomJob> logger)
        {
            _scraperService = scraperService;
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var slug = context.MergedJobDataMap.GetString(SlugKey);
            var category = ColesCategories.FindBySlug(slug)
                ?? throw new JobExecutionException($"Unknown Coles category slug '{slug}' for job {context.JobDetail.Key}");
            var attempt = context.MergedJobDataMap.ContainsKey(AttemptKey) ? context.MergedJobDataMap.GetIntValue(AttemptKey) : 1;

            await ColesDomJobLock.Gate.WaitAsync(context.CancellationToken);
            try
            {
                await _scraperService.ScrapeAsync(category, 0, context.CancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await ScheduleRetryAsync(context, attempt);
                throw;
            }
            finally
            {
                ColesDomJobLock.Gate.Release();
            }
        }

        private async Task ScheduleRetryAsync(IJobExecutionContext context, int attempt)
        {
            var maxAttempts = ReadInt("COLES_RETRY_ATTEMPTS", DefaultMaxAttempts);
            if (attempt >= maxAttempts)
            {
                _logger.LogWarning("Job {JobKey}: attempt {Attempt} of {Max} failed; no more retries", context.JobDetail.Key, attempt, maxAttempts);
                return;
            }

            var startAt = DateTimeOffset.UtcNow.AddMinutes(ReadInt("COLES_RETRY_DELAY_MINUTES", DefaultRetryDelayMinutes));
            if (ColesDomFetchGuard.Breaker.BlockedUntilUtc is DateTime blockedUntil &&
                new DateTimeOffset(blockedUntil, TimeSpan.Zero).AddMinutes(5) > startAt)
            {
                startAt = new DateTimeOffset(blockedUntil, TimeSpan.Zero).AddMinutes(5);
            }

            var trigger = TriggerBuilder.Create()
                .WithIdentity($"{context.JobDetail.Key.Name}-retry-{attempt + 1}-{startAt:yyyyMMddHHmm}")
                .ForJob(context.JobDetail.Key)
                .UsingJobData(AttemptKey, attempt + 1)
                .StartAt(startAt)
                .Build();

            try
            {
                await context.Scheduler.ScheduleJob(trigger);
                _logger.LogWarning("Job {JobKey}: attempt {Attempt} failed; retry scheduled for {StartAt:u}",
                    context.JobDetail.Key, attempt, startAt);
            }
            catch (ObjectAlreadyExistsException)
            {
                // A retry for this slot is already queued.
            }
        }

        private static int ReadInt(string name, int fallback) =>
            int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : fallback;
    }
}
