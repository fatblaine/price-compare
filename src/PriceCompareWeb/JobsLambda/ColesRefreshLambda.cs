using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using PriceCompareData.DTOs;

namespace PriceCompareWeb.JobsLambda
{
    public class ColesRefreshLambda
    {
        private readonly IColesDownScraperService _scraper;

        public ColesRefreshLambda(IColesDownScraperService scraper)
        {
            _scraper = scraper;
        }

        // AWS Lambda handler
        public async Task Handler()
        {
            await _scraper.GetAllDownDownProductsAsync(new ColesDownProductRequest());
        }
    }
}