using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PriceCompareCore.Config;
using PriceCompareCore.Interfaces;
using PriceCompareCore.Services;
using PriceCompareData.Data;
using PriceCompareData.Entities.Jobs;

namespace PriceCompareWeb.JobsLambda
{
    public class FavoritePriceTrackingLambda
    {
        private readonly IFavoritePriceTrackingService _trackingService;
        private readonly AppDbContext _db;
        private readonly ILogger<FavoritePriceTrackingService> _logger;

        public FavoritePriceTrackingLambda()
        {
            var conn = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                ?? throw new InvalidOperationException("Missing database connection string.");

            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var emailOptions = config.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
            var favoriteSettings = config.GetSection("FavoriteAlerts").Get<FavoriteAlertSettings>() ?? new FavoriteAlertSettings();

            var csb = new Npgsql.NpgsqlConnectionStringBuilder(conn) { Pooling = false };
            _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(csb.ConnectionString, o => o.CommandTimeout(120))
                .Options);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<FavoritePriceTrackingService>();

            var emailSender = new SmtpEmailSender(Options.Create(emailOptions));
            _trackingService = new FavoritePriceTrackingService(_db, emailSender, _logger, Options.Create(favoriteSettings));
        }

        public async Task Handler()
        {
            var start = DateTime.UtcNow;
            Exception? error = null;

            try
            {
                await _trackingService.CheckAndNotifyAsync();
            }
            catch (Exception ex)
            {
                error = ex;
                throw;
            }
            finally
            {
                var end = DateTime.UtcNow;
                var scheduleName = Environment.GetEnvironmentVariable("SCHEDULE_NAME");
                if (string.IsNullOrWhiteSpace(scheduleName))
                {
                    scheduleName = Environment.GetEnvironmentVariable("AWS_SCHEDULER_SCHEDULE_NAME");
                }
                var jobName = string.IsNullOrWhiteSpace(scheduleName)
                    ? "FavoritePriceTrackingJob"
                    : scheduleName;

                var run = new JobRun
                {
                    JobName = jobName,
                    Source = "aws",
                    ScheduledTime = null,
                    StartTime = start,
                    EndTime = end,
                    Status = error == null ? "success" : "fail",
                    DurationMs = (int)(end - start).TotalMilliseconds,
                    ErrorMessage = error?.GetBaseException().Message,
                    RequestId = Environment.GetEnvironmentVariable("AWS_REQUEST_ID"),
                    Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                };

                await JobRunRecorder.TryRecordAsync(_db, run, _logger);
            }
        }
    }
}
