using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.Entities;
using PriceCompareData.Entities.Compare;
using PriceCompareData.Entities.Scraping;

namespace PriceCompareCore.Interfaces
{
    public interface IIngestionService
    {
        Task UpsertColesSpecialAsync(IEnumerable<ColesSpecialProduct> items);
        Task UpsertColesDownAsync(IEnumerable<ColesDownProduct> items);
        Task UpsertWwsAsync(IEnumerable<WoolworthsSpecialProduct> items);

        IReadOnlyList<Product> MapColesSpecialProducts(IEnumerable<ColesSpecialProduct> items);
        IReadOnlyList<Product> MapColesDownProducts(IEnumerable<ColesDownProduct> items);
        IReadOnlyList<Product> MapWoolworthsProducts(IEnumerable<WoolworthsSpecialProduct> items);
    }
}
