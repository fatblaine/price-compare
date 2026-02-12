using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PriceCompareData.Entities;

namespace PriceCompareCore.Interfaces
{
    public interface IColesFruitVegetablesDomScraperService
    {
        Task<List<ColesDownProduct>> ScrapeAsync(int limit = 0, CancellationToken ct = default);
    }
}
