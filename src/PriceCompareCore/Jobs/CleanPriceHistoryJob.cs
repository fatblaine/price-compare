using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using Quartz;

namespace PriceCompareCore.Jobs
{
    public class CleanPriceHistoryJob : IJob
    {
        private readonly IColesDownScraperService _scraperService;

        public CleanPriceHistoryJob(IColesDownScraperService scraperService)
        {
            _scraperService = scraperService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await _scraperService.CleanOldPriceHistoryAsync();
        }
    }
}