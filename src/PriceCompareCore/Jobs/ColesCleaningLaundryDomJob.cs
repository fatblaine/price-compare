using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using Quartz;
using System.Threading;

namespace PriceCompareCore.Jobs
{
    public class ColesCleaningLaundryDomJob : IJob
    {
        private readonly IColesCleaningLaundryDomScraperService _scraperService;

        public ColesCleaningLaundryDomJob(IColesCleaningLaundryDomScraperService scraperService)
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
