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
using PriceCompareData.Data;
using PriceCompareData.DTOs;

namespace PriceCompareWeb.JobsLambda
{
    public class WwsRefreshSpecialLambda
    {
        private readonly IWoolworthsSpecialScraperService _scraperService;

        public WwsRefreshSpecialLambda()
        {
            // 1. Connection string from environment variable
            var conn = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(conn)
                .Options;
            var db = new AppDbContext(options);

            // 2. Initialize in-memory cache (replace Redis)
            var cacheOptions = Options.Create(new MemoryDistributedCacheOptions());
            IDistributedCache cache = new MemoryDistributedCache(cacheOptions);

            // 3. Initialize Logger
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<WoolworthsSpecialScraperService>();

            // 4. Initialize the scraper service
            _scraperService = new WoolworthsSpecialScraperService(db, logger, cache);
        }

        // AWS Lambda handler
        public async Task Handler()
        {
            await _scraperService.GetAllOnSpecialProductsAsync(new WoolworthsSpecialProductRequest());
        }
    }
}