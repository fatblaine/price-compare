using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using Quartz;

namespace PriceCompareCore.Jobs
{
    public class WwsEverydayLowPriceDomJob : IJob
    {
        private readonly IWoolworthsEverydayLowPriceDomScraperService _scraperService;

        public WwsEverydayLowPriceDomJob(IWoolworthsEverydayLowPriceDomScraperService scraperService)
        {
            _scraperService = scraperService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await _scraperService.ScrapeAsync(0, context.CancellationToken);
        }
    }
}
