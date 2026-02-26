using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.Entities.Compare;

namespace PriceCompareCore.Interfaces
{
    public interface IVectorSearchService
    {
        // Finds vector-similar candidates in target shop.
        Task<IReadOnlyList<Product>> SearchAsync(Product source, int targetShop, int topN);
    }
}