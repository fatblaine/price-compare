using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PriceCompareData.Entities.Scraping;

namespace PriceCompareCore.Interfaces
{
    public interface IWoolworthsLowerShelfDomScraperService
    {
        Task<List<WoolworthsSpecialProduct>> ScrapeAsync(int limit = 20, CancellationToken ct = default);
    }
}
