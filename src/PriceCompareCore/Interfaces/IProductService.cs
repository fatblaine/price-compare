using System.Collections.Generic;
using System.Threading.Tasks;
using PriceCompareData.Entities.Compare;

namespace PriceCompareCore.Interfaces
{
    public interface IProductService
    {
        /// <summary>
        /// Returns total count and paginated list of products matching optional filters.
        /// </summary>
        Task<(int Total, List<Product> Items)> GetProductsAsync(int page, int pageSize, string? name = null, int? shopType = null, int? categoryId = null);
    }
}
