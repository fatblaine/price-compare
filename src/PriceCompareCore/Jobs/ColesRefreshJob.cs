using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quartz;

namespace PriceCompareCore.Interfaces
{
    public class ColesRefreshJob : IJob
    {
        private readonly IScraperService _scraperService;

        public ColesRefreshJob(IScraperService scraperService)
        {
            _scraperService = scraperService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await _scraperService.GetAllDownDownProductsAsync(new PriceCompareData.DTOs.ScrapedProductRequest());
        }
    }
}