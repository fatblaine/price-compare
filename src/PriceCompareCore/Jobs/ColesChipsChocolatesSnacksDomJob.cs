using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using Quartz;
using System.Threading;

namespace PriceCompareCore.Jobs
{
    public class ColesChipsChocolatesSnacksDomJob : IJob
    {
        private readonly IColesChipsChocolatesSnacksDomScraperService _scraperService;

        public ColesChipsChocolatesSnacksDomJob(IColesChipsChocolatesSnacksDomScraperService scraperService)
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
