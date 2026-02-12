using System.Threading;
using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using Quartz;

namespace PriceCompareCore.Jobs
{
    public class ColesDairyEggsFridgeDomJob : IJob
    {
        private readonly IColesDairyEggsFridgeDomScraperService _scraperService;

        public ColesDairyEggsFridgeDomJob(IColesDairyEggsFridgeDomScraperService scraperService)
        {
            _scraperService = scraperService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await ColesDomJobLock.Gate.WaitAsync(context.CancellationToken);
            try
            {
                await _scraperService.ScrapeAsync(0, context.CancellationToken);
            }
            finally
            {
                ColesDomJobLock.Gate.Release();
            }
        }
    }
}
