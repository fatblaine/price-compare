using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
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

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(conn)
                .Options;
            var db = new AppDbContext(options);

            IDistributedCache cache = CacheFactory.Create();

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var scraperLogger = loggerFactory.CreateLogger<ColesSpecialScraperService>();
            var mapperLogger = loggerFactory.CreateLogger<CategoryMappingService>();

            var httpClient = new HttpClient();
            var mapper = new CategoryMappingService(db, mapperLogger);
            var ingestion = new IngestionService(db, mapper);

            _scraperService = new ColesSpecialScraperService(
                httpClient,
                scraperLogger,
                cache,
                db,
                ingestion
            );
        }

        // AWS Lambda handler
        public async Task Handler()
        {
            await _scraperService.GetAllOnSpecialProductsAsync(new ColesSpecialProductRequest());
        }
    }
}