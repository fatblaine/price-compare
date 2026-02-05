using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using Quartz;

namespace PriceCompareCore.Jobs
{
    public class WwsLowerShelfDomJob : IJob
    {
        private readonly IWoolworthsLowerShelfDomScraperService _scraperService;

        public WwsLowerShelfDomJob(IWoolworthsLowerShelfDomScraperService scraperService)
        {
            _scraperService = scraperService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await _scraperService.ScrapeAsync(0, context.CancellationToken);
        }
    }
}
