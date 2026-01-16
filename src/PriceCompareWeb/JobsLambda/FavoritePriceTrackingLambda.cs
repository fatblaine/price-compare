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

namespace PriceCompareWeb.JobsLambda
{
    public class FavoritePriceTrackingLambda
    {
        private readonly IFavoritePriceTrackingService _trackingService;

        public FavoritePriceTrackingLambda()
        {
            var conn = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
            if (string.IsNullOrWhiteSpace(conn))
                throw new InvalidOperationException("Missing database connection string.");

            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var emailOptions = config.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
            var favoriteSettings = config.GetSection("FavoriteAlerts").Get<FavoriteAlertSettings>() ?? new FavoriteAlertSettings();

            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(conn)
                .Options);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<FavoritePriceTrackingService>();

            var emailSender = new SmtpEmailSender(Options.Create(emailOptions));
            _trackingService = new FavoritePriceTrackingService(db, emailSender, logger, Options.Create(favoriteSettings));
        }

        public Task Handler()
        {
            return _trackingService.CheckAndNotifyAsync();
        }
    }
}