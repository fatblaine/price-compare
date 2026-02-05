using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceCompareCore.Interfaces;
using PriceCompareData.Data;
using PriceCompareData.Entities.Compare;

namespace PriceCompareCore.Services
{
    public class ProductService : IProductService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ProductService> _logger;

        public ProductService(AppDbContext db, ILogger<ProductService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<(int Total, List<Product> Items)> GetProductsAsync(int page, int pageSize, string? name = null, int? shopType = null, int? categoryId = null)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;

            try
            {
                var query = _db.Products.AsNoTracking().AsQueryable();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    // Use ILike for PostgreSQL case-insensitive matching (Npgsql)
                    query = query.Where(p => p.Name != null && EF.Functions.ILike(p.Name, $"%{name}%"));
                }

                if (shopType.HasValue)
                    query = query.Where(p => p.ShopType == shopType.Value);

                if (categoryId.HasValue)
                    query = query.Where(p => p.CategoryId == categoryId.Value);

                using var countCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                _logger.LogInformation("GetProducts count start");
                var total = await query.CountAsync(countCts.Token);
                _logger.LogInformation("GetProducts count done total={Total}", total);

                var hasFilter = !string.IsNullOrWhiteSpace(name) || shopType.HasValue || categoryId.HasValue;
                // Avoid full-table sort by name when listing all products; use PK order unless filtered.
                var orderedQuery = hasFilter
                    ? query.OrderBy(p => p.Name).ThenBy(p => p.ProductId)
                    : query.OrderBy(p => p.ProductId);

                var itemsQuery = orderedQuery
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize);
                _logger.LogInformation("GetProducts items query: {Sql}", itemsQuery.ToQueryString());

                try
                {
                    using var itemsCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    _logger.LogInformation("GetProducts items start page={Page} pageSize={PageSize}", page, pageSize);
                    var items = await itemsQuery.ToListAsync(itemsCts.Token);
                    _logger.LogInformation("GetProducts items done items={Count}", items.Count);
                    return (total, items);
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogWarning(ex, "GetProducts items timed out; returning empty list.");
                    return (total, new List<Product>());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying products");
                throw;
            }
        }
    }
}
