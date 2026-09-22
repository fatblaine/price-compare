using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Scheduler;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PriceCompareData.Data;
using PriceCompareData.Entities.Jobs;
using PriceCompareWeb.Services;

namespace PriceCompareTests
{
    public class AdminScheduleServiceTests
    {
        private static AppDbContext CreateDb()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        // No ISchedulerFactory registered — same as the API Lambda with Scraping__EnableQuartz=false.
        private static AdminScheduleService CreateService(AppDbContext db) =>
            new AdminScheduleService(
                db,
                new Mock<IAmazonScheduler>().Object,
                new ServiceCollection().BuildServiceProvider(),
                NullLogger<AdminScheduleService>.Instance);

        private static JobDefinition Definition(string jobName, string source, string cron, bool enabled = true) =>
            new JobDefinition
            {
                JobName = jobName,
                Source = source,
                ScheduleExpression = cron,
                Timezone = "Australia/Sydney",
                Enabled = enabled,
                Description = $"{jobName} description",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

        [Fact]
        public async Task GetSchedulesAsync_WithoutScheduler_ListsQuartzJobsFromJobDefinitions()
        {
            using var db = CreateDb();
            db.JobDefinitions.AddRange(
                Definition("ColesRefreshJob", "quartz", "0 5 0 ? * WED"),
                Definition("WwsSummerPriceDomJob", "quartz", "0 45 6 ? * WED", enabled: false),
                Definition("price-compare-prod-favorite-price-tracking", "aws", "cron(55 6 ? * WED *)"));
            await db.SaveChangesAsync();

            var result = await CreateService(db).GetSchedulesAsync("quartz", CancellationToken.None);

            Assert.Equal(2, result.Count);
            Assert.All(result, r => Assert.Equal("quartz", r.Source));

            var coles = result.Single(r => r.JobName == "ColesRefreshJob");
            Assert.Equal("0 5 0 ? * WED", coles.ScheduleExpression);
            Assert.Equal("Australia/Sydney", coles.Timezone);
            Assert.True(coles.Enabled);
            Assert.Equal("ColesRefreshJob description", coles.Description);
            Assert.NotNull(coles.NextFireTimeUtc);

            // Next fire time is computed in the definition's timezone: Wednesday 00:05 Sydney time.
            var sydney = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");
            var local = TimeZoneInfo.ConvertTimeFromUtc(coles.NextFireTimeUtc!.Value, sydney);
            Assert.Equal(DayOfWeek.Wednesday, local.DayOfWeek);
            Assert.Equal(new TimeSpan(0, 5, 0), local.TimeOfDay);
            Assert.True(coles.NextFireTimeUtc > DateTime.UtcNow);

            var summer = result.Single(r => r.JobName == "WwsSummerPriceDomJob");
            Assert.False(summer.Enabled);
            Assert.Null(summer.NextFireTimeUtc);
        }

        [Fact]
        public async Task GetSchedulesAsync_WithInvalidCron_ReturnsRowWithoutNextFireTime()
        {
            using var db = CreateDb();
            db.JobDefinitions.Add(Definition("BrokenJob", "quartz", "not a cron"));
            await db.SaveChangesAsync();

            var result = await CreateService(db).GetSchedulesAsync("quartz", CancellationToken.None);

            var broken = Assert.Single(result);
            Assert.Equal("BrokenJob", broken.JobName);
            Assert.Null(broken.NextFireTimeUtc);
        }

        [Fact]
        public async Task GetSchedulesAsync_WithoutScheduler_AttachesLatestQuartzRun()
        {
            using var db = CreateDb();
            db.JobDefinitions.Add(Definition("ColesDeliDomJob", "quartz", "0 55 0 ? * WED"));
            var older = DateTime.UtcNow.AddDays(-14);
            var latest = DateTime.UtcNow.AddDays(-7);
            db.JobRuns.AddRange(
                new JobRun { JobName = "ColesDeliDomJob", Source = "quartz", StartTime = older, Status = "fail", ErrorMessage = "old", CreatedAt = older },
                new JobRun { JobName = "ColesDeliDomJob", Source = "quartz", StartTime = latest, Status = "success", DurationMs = 1234, CreatedAt = latest });
            await db.SaveChangesAsync();

            var result = await CreateService(db).GetSchedulesAsync("quartz", CancellationToken.None);

            var deli = Assert.Single(result);
            Assert.Equal(latest, deli.LastRunTimeUtc);
            Assert.Equal("success", deli.LastRunStatus);
            Assert.Equal(1234, deli.LastRunDurationMs);
            Assert.Null(deli.LastRunErrorMessage);
        }
    }
}
