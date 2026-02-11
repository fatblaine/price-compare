using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using Quartz;
using System.Threading;

namespace PriceCompareCore.Jobs
{
    public class ColesDrinksDomJob : IJob
    {
        private readonly IColesDrinksDomScraperService _scraperService;

        public ColesDrinksDomJob(IColesDrinksDomScraperService scraperService)
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
