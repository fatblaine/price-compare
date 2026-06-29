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
                // Fix: MAX(LastSeenAt) raw timestamp — join on ShopType only, then range-filter.
                // The old DATE(lastseenat) on both sides of the JOIN made IX_Products_ShopType_LastSeenAt
                // unusable, causing a full table scan on every request (root cause of Disk IO exhaustion).
                // With p.LastSeenAt >= maxAt.Date the indexed column stays on the left of >=,
                // so PostgreSQL does an index range scan per shop type instead.
                var latestPerShop = _db.Products
                    .AsNoTracking()
                    .Where(p => p.ShopType.HasValue)
                    .GroupBy(p => p.ShopType)
                    .Select(g => new { ShopType = g.Key, MaxAt = g.Max(p => p.LastSeenAt) });

                var query = _db.Products
                    .AsNoTracking()
                    .Join(
                        latestPerShop,
                        p => p.ShopType,
                        l => l.ShopType,
                        (p, l) => new { Product = p, MaxAt = l.MaxAt }
                    )
                    .Where(x => x.Product.LastSeenAt >= x.MaxAt.Date)
                    .Select(x => x.Product);

                if (!string.IsNullOrWhiteSpace(name))
                    query = query.Where(p => p.Name != null && EF.Functions.ILike(p.Name, $"%{name}%"));

                if (shopType.HasValue)
                    query = query.Where(p => p.ShopType == shopType.Value);

                if (categoryId.HasValue)
                    query = query.Where(p => p.CategoryId == categoryId.Value);

                using var countCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                _logger.LogInformation("GetProducts count start");
                var total = await query.CountAsync(countCts.Token);
                _logger.LogInformation("GetProducts count done total={Total}", total);

                var hasFilter = !string.IsNullOrWhiteSpace(name) || shopType.HasValue || categoryId.HasValue;

                // same_product-first ordering removed (BTS-148).
                // Previously this prefetched ALL same_product source IDs (a full scan of the 276k-row
                // productmatch table on every request) and used a multi-thousand-element = ANY(@ids)
                // expression inside ORDER BY. That forced PostgreSQL to read and sort the entire
                // current-product set before paging, which timed out (30s) once the instance was
                // throttled to baseline disk IO — returning empty "no products" and never caching,
                // so every subsequent request re-ran the heavy query (a self-sustaining IO death spiral).
                // Ordering now uses only (Name, ProductId), which can use the IX_Products_ShopType_Name
                // index when a shop filter is applied. The frontend (ProductsPage useMemo over
                // compareDataMap) already sorts same_product matches first within the visible page, so
                // only cross-page match concentration is dropped — an acceptable trade-off.
                IQueryable<Product> orderedQuery = hasFilter
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
