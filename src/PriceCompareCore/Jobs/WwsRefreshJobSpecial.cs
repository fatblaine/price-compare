using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using PriceCompareData.DTOs;
using Quartz;

namespace PriceCompareCore.Jobs
{
    public class WwsRefreshJobSpecial : IJob
    {
        private readonly IWoolworthsSpecialScraperService _scraperService;

        public WwsRefreshJobSpecial(IWoolworthsSpecialScraperService scraperService)
        {
            _scraperService = scraperService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await _scraperService.GetAllOnSpecialProductsAsync(new WoolworthsSpecialProductRequest());
        }
    }
}