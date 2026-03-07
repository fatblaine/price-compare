using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PriceCompareData.Entities.Scraping;

namespace PriceCompareCore.Interfaces
{
    public interface IWoolworthsAutumnPriceDomScraperService
    {
        Task<List<WoolworthsSpecialProduct>> ScrapeAsync(int limit = 0, CancellationToken ct = default);
    }
}
