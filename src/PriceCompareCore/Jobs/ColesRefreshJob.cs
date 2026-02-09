using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quartz;

namespace PriceCompareCore.Interfaces
{
    public class ColesRefreshJob : IJob
    {
        private readonly IColesDownDomScraperService _scraperService;

        public ColesRefreshJob(IColesDownDomScraperService scraperService)
        {
            _scraperService = scraperService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await _scraperService.ScrapeAsync(0, context.CancellationToken);
        }
    }
}
