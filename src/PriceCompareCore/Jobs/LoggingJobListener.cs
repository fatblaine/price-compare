using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PriceCompareCore.Services;
using PriceCompareData.Data;
using PriceCompareData.Entities.Jobs;
using Quartz;

namespace PriceCompareCore.Jobs
{
    public class LoggingJobListener : IJobListener
    {
        private readonly ILogger<LoggingJobListener> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public LoggingJobListener(ILogger<LoggingJobListener> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public string Name => "LoggingJobListener";

        public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken ct = default) => Task.CompletedTask;

        public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken ct = default)
        {
            _logger.LogInformation("Job {JobKey} is triggered at {FireTimeUtc:u}",
                                context.JobDetail.Key,
                                context.FireTimeUtc);
            return Task.CompletedTask;
        }

        public async Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken ct = default)
        {
            if (jobException != null)
            {
                _logger.LogError(jobException, "Job {JobKey} failed", context.JobDetail.Key);
            }
            else
            {
                _logger.LogInformation("Job {JobKey} succeeded，next execution time is {NextFireTimeUtc:u}",
                                    context.JobDetail.Key,
                                    context.NextFireTimeUtc);
            }

            var start = context.FireTimeUtc?.UtcDateTime ?? DateTime.UtcNow;
            var end = start + context.JobRunTime;

            var run = new JobRun
            {
                JobName = context.JobDetail.Key.Name,
                Source = "quartz",
                ScheduledTime = context.ScheduledFireTimeUtc?.UtcDateTime,
                StartTime = start,
                EndTime = end,
                Status = jobException == null ? "success" : "fail",
                DurationMs = (int)context.JobRunTime.TotalMilliseconds,
                ErrorMessage = jobException?.GetBaseException().Message,
                RequestId = context.FireInstanceId,
                Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            };

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await JobRunRecorder.TryRecordAsync(db, run, _logger, ct);
        }

    }

}
