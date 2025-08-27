using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quartz;

namespace PriceCompareCore.Interfaces
{
    public class ColesRefreshJob : IJob
    {
        private readonly IColesDownScraperService _scraperService;

        public ColesRefreshJob(IColesDownScraperService scraperService)
        {
            _scraperService = scraperService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await _scraperService.GetAllDownDownProductsAsync(new PriceCompareData.DTOs.ColesDownProductRequest());
        }
    }
}