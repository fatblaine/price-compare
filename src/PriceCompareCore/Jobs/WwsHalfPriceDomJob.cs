using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using Quartz;

namespace PriceCompareCore.Jobs
{
    public class WwsHalfPriceDomJob : IJob
    {
        private readonly IWoolworthsHalfPriceDomScraperService _scraperService;

        public WwsHalfPriceDomJob(IWoolworthsHalfPriceDomScraperService scraperService)
        {
            _scraperService = scraperService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await _scraperService.ScrapeAsync(0, context.CancellationToken);
        }
    }
}
