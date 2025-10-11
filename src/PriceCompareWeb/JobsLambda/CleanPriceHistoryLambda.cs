using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PriceCompareCore.Interfaces;
using PriceCompareCore.Services;
using PriceCompareData.Data;

namespace PriceCompareWeb.JobsLambda
{
    public class CleanPriceHistoryLambda
    {
        private readonly IColesDownScraperService _scraperService;

        public CleanPriceHistoryLambda()
        {
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var conn = config.GetConnectionString("DefaultConnection")
                       ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

            if (string.IsNullOrWhiteSpace(conn))
                throw new InvalidOperationException("Missing database connection string.");

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(conn)
                .Options;
            var db = new AppDbContext(options);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<ColesDownScraperService>();

            _scraperService = new ColesDownScraperService(db, logger);
        }

        // AWS Lambda handler
        public async Task Handler()
        {
            await _scraperService.CleanOldPriceHistoryAsync();
        }
    }
}