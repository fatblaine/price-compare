using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PriceCompareData.Entities;

namespace PriceCompareCore.Interfaces
{
    public interface IColesHealthBeautyDomScraperService
    {
        Task<List<ColesDownProduct>> ScrapeAsync(int limit = 0, CancellationToken ct = default);
    }
}
