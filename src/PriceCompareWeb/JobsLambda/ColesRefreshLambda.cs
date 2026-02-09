using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using PriceCompareCore.Config;
using PriceCompareCore.Interfaces;
using PriceCompareCore.Services;
using PriceCompareCore.Utils;
using PriceCompareData.Data;
using PriceCompareData.DTOs;

namespace PriceCompareWeb.JobsLambda
{
    public class ColesRefreshLambda
    {
        private readonly IColesDownDomScraperService _scraper;

        public ColesRefreshLambda()
        {
            // Connection string
            var conn = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
            if (string.IsNullOrWhiteSpace(conn))
                throw new InvalidOperationException("Missing database connection string.");

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(conn)
                .Options;
            var db = new AppDbContext(options);

            // Cache（Redis or Memory）
            IDistributedCache cache = CacheFactory.Create();

            // Logger factory
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var scraperLogger = loggerFactory.CreateLogger<ColesDownDomScraperService>();
            var mapperLogger = loggerFactory.CreateLogger<CategoryMappingService>();
            var exportLogger = loggerFactory.CreateLogger<ScrapeExportService>();

            // Dependency injection
            var httpClient = new HttpClient();
            var mapper = new CategoryMappingService(db, mapperLogger);
            var ingestion = new IngestionService(db, mapper);
            var export = new ScrapeExportService(
                Options.Create(new ScrapeExportOptions { Enabled = false }),
                exportLogger);

            _scraper = new ColesDownDomScraperService(
                httpClient,
                scraperLogger,
                cache,
                db,
                ingestion,
                export
            );
        }

        // AWS Lambda handler
        public async Task Handler()
        {
            await _scraper.ScrapeAsync(0, CancellationToken.None);
        }
    }
}
