using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PriceCompareCore.Services;
using PriceCompareData.Entities;

namespace PriceCompareCore.Interfaces
{
    public interface IColesCategoryScraperService
    {
        /// <summary>
        /// Scrapes every page of one Coles category, saves it to pricehistory/Products and exports it.
        /// Throws <see cref="PriceCompareCore.Exceptions.ColesScrapeException"/> when the scrape fails.
        /// </summary>
        Task<List<ColesDownProduct>> ScrapeAsync(ColesCategory category, int limit = 0, CancellationToken ct = default);
    }

    /// <summary>
    /// Supplies the raw Next.js JSON (<c>{"pageProps": {...}}</c>) for one page of a Coles category.
    /// </summary>
    public interface IColesCategoryPageSource
    {
        Task<string> GetPageJsonAsync(ColesCategory category, int page, CancellationToken ct);
    }
}
