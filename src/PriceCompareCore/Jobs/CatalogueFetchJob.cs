using System;
using System.Threading.Tasks;
using PriceCompareCore.Services.Catalogue;
using PriceCompareCore.Utils;
using Quartz;

namespace PriceCompareCore.Jobs
{
    /// <summary>
    /// Local fallback for the next-week catalogue fetch (BTS-163). Production runs the same service
    /// from the price-catalogue Lambda; this job is registered only when
    /// Scraping:EnableCatalogueSchedule=true. Failures surface in job_runs through LoggingJobListener.
    /// </summary>
    [DisallowConcurrentExecution]
    public sealed class CatalogueFetchJob : IJob
    {
        public const string ShopTypeKey = "shopType";

        private readonly ICatalogueFetchService _service;

        public CatalogueFetchJob(ICatalogueFetchService service)
        {
            _service = service;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var shopType = context.MergedJobDataMap.GetInt(ShopTypeKey);
            await _service.RunAsync(shopType, FailIfNotPublished(SydneyClock.Today()), context.CancellationToken);
        }

        /// <summary>
        /// Monday runs may be early (published at 17:00); Tuesday's run is the last chance before
        /// the catalogue starts, so a missing catalogue then is a failure.
        /// </summary>
        public static bool FailIfNotPublished(DateOnly sydneyToday) => sydneyToday.DayOfWeek == DayOfWeek.Tuesday;
    }
}
