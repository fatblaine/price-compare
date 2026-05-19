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
    public class ColesRefreshSpecialLambda
    {
        private readonly IColesSpecialScraperService _scraperService;

        public ColesRefreshSpecialLambda()
        {
            var conn = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
            if (string.IsNullOrWhiteSpace(conn))
                throw new InvalidOperationException("Missing database connection string.");

            var csb = new Npgsql.NpgsqlConnectionStringBuilder(conn) { Pooling = false };
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(csb.ConnectionString, o => o.CommandTimeout(120))
                .Options;
            var db = new AppDbContext(options);

            IDistributedCache cache = CacheFactory.Create();

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var scraperLogger = loggerFactory.CreateLogger<ColesSpecialScraperService>();
            var mapperLogger = loggerFactory.CreateLogger<CategoryMappingService>();
            var exportLogger = loggerFactory.CreateLogger<ScrapeExportService>();

            var mapper = new CategoryMappingService(db, mapperLogger);
            var ingestion = new IngestionService(db, mapper);
            var export = new ScrapeExportService(
                Options.Create(new ScrapeExportOptions { Enabled = false }),
                exportLogger);

            _scraperService = new ColesSpecialScraperService(
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
            await _scraperService.GetAllOnSpecialProductsAsync(new ColesSpecialProductRequest());
        }
    }
}
