using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using Quartz;

namespace PriceCompareCore.Jobs
{
    public class WwsAutumnPriceDomJob : IJob
    {
        private readonly IWoolworthsAutumnPriceDomScraperService _scraperService;

        public WwsAutumnPriceDomJob(IWoolworthsAutumnPriceDomScraperService scraperService)
            => _scraperService = scraperService;

        public async Task Execute(IJobExecutionContext context)
        {
            await ColesDomJobLock.Gate.WaitAsync(context.CancellationToken);
            try { await _scraperService.ScrapeAsync(0, context.CancellationToken); }
            finally { ColesDomJobLock.Gate.Release(); }
        }
    }
}
