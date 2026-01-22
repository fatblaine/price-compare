using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Scheduler;
using Amazon.Scheduler.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;
using PriceCompareData.Data;
using PriceCompareData.Entities.Jobs;
using PriceCompareWeb.Controllers.Models;

namespace PriceCompareWeb.Services
{
    public class AdminScheduleService
    {
        private readonly AppDbContext _db;
        private readonly IAmazonScheduler _awsScheduler;
        private readonly IServiceProvider _services;
        private readonly ILogger<AdminScheduleService> _logger;

        public AdminScheduleService(
            AppDbContext db,
            IAmazonScheduler awsScheduler,
            IServiceProvider services,
            ILogger<AdminScheduleService> logger)
        {
            _db = db;
            _awsScheduler = awsScheduler;
            _services = services;
            _logger = logger;
        }

        public async Task<List<JobScheduleDto>> GetSchedulesAsync(string? source, CancellationToken ct)
        {
            var defs = await _db.JobDefinitions.ToListAsync(ct);
            var defMap = defs.ToDictionary(
                d => (d.JobName, d.Source),
                d => d);

            var result = new List<JobScheduleDto>();

            if (string.IsNullOrWhiteSpace(source) || source == "aws")
            {
                try
                {
                    result.AddRange(await GetAwsSchedulesAsync(defMap, ct));
                }
                catch (Exception ex)
                {
                    // Do not fail the admin endpoint if AWS access is missing.
                    _logger.LogWarning(ex, "Failed to load AWS schedules.");
                }
            }

            if (string.IsNullOrWhiteSpace(source) || source == "quartz")
            {
                try
                {
                    result.AddRange(await GetQuartzSchedulesAsync(defMap, ct));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load Quartz schedules.");
                }
            }

            await AttachLatestRunsAsync(result, ct);

            return result
                .OrderBy(r => r.Source)
                .ThenBy(r => r.JobName)
                .ToList();
        }

        private async Task<List<JobScheduleDto>> GetAwsSchedulesAsync(
            Dictionary<(string JobName, string Source), JobDefinition> defMap,
            CancellationToken ct)
        {
            var output = new List<JobScheduleDto>();
            string? nextToken = null;

            do
            {
                var listResp = await _awsScheduler.ListSchedulesAsync(new ListSchedulesRequest
                {
                    NextToken = nextToken
                }, ct);

                foreach (var summary in listResp.Schedules)
                {
                    var detail = await _awsScheduler.GetScheduleAsync(new GetScheduleRequest
                    {
                        Name = summary.Name
                    }, ct);

                    var jobName = summary.Name;
                    defMap.TryGetValue((jobName, "aws"), out var def);

                    var stateValue = detail.State?.ToString();
                    var enabled = string.Equals(stateValue, "ENABLED", StringComparison.OrdinalIgnoreCase);

                    var scheduleExpression = detail.ScheduleExpression ?? def?.ScheduleExpression ?? string.Empty;
                    var timezone = detail.ScheduleExpressionTimezone ?? def?.Timezone ?? "UTC";
                    var nextFireUtc = ComputeNextAwsFireTimeUtc(scheduleExpression, timezone);

                    output.Add(new JobScheduleDto(
                        JobName: jobName,
                        Source: "aws",
                        ScheduleExpression: scheduleExpression,
                        Timezone: timezone,
                        Enabled: enabled,
                        Description: def?.Description,
                        NextFireTimeUtc: nextFireUtc,
                        LastRunTimeUtc: null,
                        LastRunStatus: null,
                        LastRunDurationMs: null,
                        LastRunErrorMessage: null));
                }

                nextToken = listResp.NextToken;
            } while (!string.IsNullOrWhiteSpace(nextToken));

            return output;
        }

        private DateTime? ComputeNextAwsFireTimeUtc(string scheduleExpression, string timezoneId)
        {
            if (string.IsNullOrWhiteSpace(scheduleExpression))
            {
                return null;
            }

            try
            {
                if (scheduleExpression.StartsWith("cron(", StringComparison.OrdinalIgnoreCase))
                {
                    var cron = scheduleExpression.Substring(5, scheduleExpression.Length - 6).Trim();
                    var parts = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 6)
                    {
                        // AWS cron has 6 fields (min hour dom month dow year); Quartz expects seconds first.
                        cron = "0 " + cron;
                    }

                    var cronExpression = new CronExpression(cron)
                    {
                        TimeZone = ResolveTimeZone(timezoneId)
                    };

                    var next = cronExpression.GetNextValidTimeAfter(DateTimeOffset.UtcNow);
                    return next?.UtcDateTime;
                }

                if (scheduleExpression.StartsWith("rate(", StringComparison.OrdinalIgnoreCase))
                {
                    var inner = scheduleExpression.Substring(5, scheduleExpression.Length - 6).Trim();
                    var parts = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[0], out var value))
                    {
                        var unit = parts[1].ToLowerInvariant();
                        var interval = unit switch
                        {
                            "minute" or "minutes" => TimeSpan.FromMinutes(value),
                            "hour" or "hours" => TimeSpan.FromHours(value),
                            "day" or "days" => TimeSpan.FromDays(value),
                            _ => (TimeSpan?)null
                        };

                        if (interval.HasValue)
                        {
                            return DateTime.UtcNow.Add(interval.Value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to compute next fire time for schedule: {ScheduleExpression}", scheduleExpression);
            }

            return null;
        }

        private static TimeZoneInfo ResolveTimeZone(string timezoneId)
        {
            if (string.IsNullOrWhiteSpace(timezoneId))
            {
                return TimeZoneInfo.Utc;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.Utc;
            }
            catch (InvalidTimeZoneException)
            {
                return TimeZoneInfo.Utc;
            }
        }

        private async Task<List<JobScheduleDto>> GetQuartzSchedulesAsync(
            Dictionary<(string JobName, string Source), JobDefinition> defMap,
            CancellationToken ct)
        {
            var schedulerFactory = _services.GetService<ISchedulerFactory>();
            if (schedulerFactory == null)
            {
                return new List<JobScheduleDto>();
            }

            var scheduler = await schedulerFactory.GetScheduler(ct);
            var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), ct);
            var output = new List<JobScheduleDto>();

            foreach (var jobKey in jobKeys)
            {
                var triggers = await scheduler.GetTriggersOfJob(jobKey, ct);

                var nextFire = triggers
                    .Select(t => t.GetNextFireTimeUtc())
                    .Where(t => t.HasValue)
                    .OrderBy(t => t!.Value)
                    .FirstOrDefault();

                var cronTrigger = triggers.OfType<ICronTrigger>().FirstOrDefault();
                var cronExpr = cronTrigger?.CronExpressionString ?? string.Empty;
                var timeZone = cronTrigger?.TimeZone?.Id ?? "UTC";

                var enabled = triggers.Count > 0;
                foreach (var trigger in triggers)
                {
                    var state = await scheduler.GetTriggerState(trigger.Key, ct);
                    if (state == TriggerState.Paused)
                    {
                        enabled = false;
                        break;
                    }
                }

                defMap.TryGetValue((jobKey.Name, "quartz"), out var def);

                output.Add(new JobScheduleDto(
                    JobName: jobKey.Name,
                    Source: "quartz",
                    ScheduleExpression: cronExpr,
                    Timezone: timeZone,
                    Enabled: enabled,
                    Description: def?.Description,
                    NextFireTimeUtc: nextFire?.UtcDateTime,
                    LastRunTimeUtc: null,
                    LastRunStatus: null,
                    LastRunDurationMs: null,
                    LastRunErrorMessage: null));
            }

            return output;
        }

        private async Task AttachLatestRunsAsync(List<JobScheduleDto> schedules, CancellationToken ct)
        {
            if (schedules.Count == 0)
            {
                return;
            }

            var latestRuns = new Dictionary<(string JobName, string Source), JobRun>();
            var keysBySource = schedules
                .GroupBy(s => s.Source)
                .ToList();

            foreach (var group in keysBySource)
            {
                var jobNames = group
                    .Select(s => s.JobName)
                    .Distinct()
                    .ToList();

                if (jobNames.Count == 0)
                {
                    continue;
                }

                var runs = await _db.JobRuns
                    .Where(r => r.Source == group.Key && jobNames.Contains(r.JobName))
                    .OrderByDescending(r => r.StartTime)
                    .ToListAsync(ct);

                foreach (var run in runs)
                {
                    var key = (run.JobName, run.Source);
                    if (!latestRuns.ContainsKey(key))
                    {
                        latestRuns[key] = run;
                    }
                }
            }

            for (var i = 0; i < schedules.Count; i++)
            {
                var schedule = schedules[i];
                if (latestRuns.TryGetValue((schedule.JobName, schedule.Source), out var run))
                {
                    schedules[i] = schedule with
                    {
                        LastRunTimeUtc = run.StartTime,
                        LastRunStatus = run.Status,
                        LastRunDurationMs = run.DurationMs,
                        LastRunErrorMessage = run.ErrorMessage
                    };
                }
            }
        }
    }
}
