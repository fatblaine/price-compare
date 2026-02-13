using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using Quartz;

namespace PriceCompareCore.Jobs
{
    public class WwsSummerPriceDomJob : IJob
    {
        private readonly IWoolworthsSummerPriceDomScraperService _scraperService;

        public WwsSummerPriceDomJob(IWoolworthsSummerPriceDomScraperService scraperService)
        {
            _scraperService = scraperService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await _scraperService.ScrapeAsync(0, context.CancellationToken);
        }
    }
}
