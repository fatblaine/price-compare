using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareCore.Interfaces;

namespace PriceCompareWeb.JobsLambda
{
    public class CleanPriceHistoryLambda
    {
        private readonly IColesDownScraperService _scraperService;

        public CleanPriceHistoryLambda(IColesDownScraperService scraperService)
        {
            _scraperService = scraperService;
        }

        // AWS Lambda handler
        public async Task Handler()
        {
            await _scraperService.CleanOldPriceHistoryAsync();
        }
    }
}