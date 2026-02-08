using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PriceCompareData.Entities.Scraping;

namespace PriceCompareCore.Interfaces
{
    public interface IWoolworthsHalfPriceDomScraperService
    {
        Task<List<WoolworthsSpecialProduct>> ScrapeAsync(int limit = 0, CancellationToken ct = default);
    }
}
