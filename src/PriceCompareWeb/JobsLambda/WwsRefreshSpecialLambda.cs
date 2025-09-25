using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using PriceCompareData.DTOs;

namespace PriceCompareWeb.JobsLambda
{
    public class WwsRefreshSpecialLambda
    {
        private readonly IWoolworthsSpecialScraperService _scraperService;

        public WwsRefreshSpecialLambda(IWoolworthsSpecialScraperService scraperService)
        {
            _scraperService = scraperService;
        }

        // AWS Lambda handler
        public async Task Handler()
        {
            await _scraperService.GetAllOnSpecialProductsAsync(new WoolworthsSpecialProductRequest());
        }
    }
}