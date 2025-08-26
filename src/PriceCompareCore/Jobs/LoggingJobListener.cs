using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;

namespace PriceCompareCore.Jobs
{
    public class LoggingJobListener : IJobListener
    {
        private readonly ILogger<LoggingJobListener> _logger;

        public LoggingJobListener(ILogger<LoggingJobListener> logger)
        {
            _logger = logger;
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

        public Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken ct = default)
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
            return Task.CompletedTask;
        }

    }

}
