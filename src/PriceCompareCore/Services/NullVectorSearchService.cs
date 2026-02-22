using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using PriceCompareData.Entities.Compare;
namespace PriceCompareCore.Services
{
    public class NullVectorSearchService : IVectorSearchService
    {
        // Vector search is disabled. Always returns empty list.
        public Task<IReadOnlyList<Product>> SearchAsync(Product source, int targetShop, int topN)
        {
            return Task.FromResult<IReadOnlyList<Product>>(new List<Product>());
        }
    }
}