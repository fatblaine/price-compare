using System.Threading;
using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using Quartz;

namespace PriceCompareCore.Jobs
{
    public class ColesDeliDomJob : IJob
    {
        private readonly IColesDeliDomScraperService _scraperService;

        public ColesDeliDomJob(IColesDeliDomScraperService scraperService)
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
