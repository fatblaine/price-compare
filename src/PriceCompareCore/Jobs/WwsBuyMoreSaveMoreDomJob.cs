using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using Quartz;

namespace PriceCompareCore.Jobs
{
    public class WwsBuyMoreSaveMoreDomJob : IJob
    {
        private readonly IWoolworthsBuyMoreSaveMoreDomScraperService _scraperService;

        public WwsBuyMoreSaveMoreDomJob(IWoolworthsBuyMoreSaveMoreDomScraperService scraperService)
        {
            _scraperService = scraperService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await _scraperService.ScrapeAsync(0, context.CancellationToken);
        }
    }
}
