using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using Quartz;

namespace PriceCompareCore.Jobs
{
    public class FavoritePriceTrackingJob : IJob
    {
        private readonly IFavoritePriceTrackingService _trackingService;

        public FavoritePriceTrackingJob(IFavoritePriceTrackingService trackingService)
        {
            _trackingService = trackingService;
        }

        public Task Execute(IJobExecutionContext context)
        {
            return _trackingService.CheckAndNotifyAsync();
        }
    }
}