using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PriceCompareCore.Interfaces;
using PriceCompareCore.Services;
using PriceCompareCore.Utils;
using PriceCompareData.Data;
using PriceCompareData.DTOs;

namespace PriceCompareWeb.JobsLambda
{
    public class WwsRefreshSpecialLambda
    {
        private readonly IWoolworthsSpecialScraperService _scraperService;

        public WwsRefreshSpecialLambda()
        {
            // 1. Database context
            var conn = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
            if (string.IsNullOrWhiteSpace(conn))
                throw new InvalidOperationException("Missing database connection string.");

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(conn)
                .Options;
            var db = new AppDbContext(options);

            // 2. Cache factory (auto-select Redis or in-memory cache)
            IDistributedCache cache = CacheFactory.Create();

            // 3. Logger factory
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var scraperLogger = loggerFactory.CreateLogger<WoolworthsSpecialScraperService>();
            var mapperLogger = loggerFactory.CreateLogger<CategoryMappingService>();

            // 4. Dependency injection
            var mapper = new CategoryMappingService(db, mapperLogger);
            var ingestion = new IngestionService(db, mapper);

            // 5. Initialize Scraper
            _scraperService = new WoolworthsSpecialScraperService(
                scraperLogger,
                cache,
                db,
                ingestion
                );
        }

        // Lambda handler
        public async Task Handler()
        {
            await _scraperService.GetAllOnSpecialProductsAsync(new WoolworthsSpecialProductRequest());
        }
    }
}