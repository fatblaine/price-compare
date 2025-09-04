using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using PriceCompareData.DTOs;
using Quartz;

namespace PriceCompareCore.Jobs
{
    public class ColesRefreshJobSpecial : IJob
    {
        private readonly IColesSpecialScraperService _scraperService;

        public ColesRefreshJobSpecial(IColesSpecialScraperService scraperService)
        {
            _scraperService = scraperService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await _scraperService.GetAllOnSpecialProductsAsync(new ColesSpecialProductRequest());
        }
    }
}