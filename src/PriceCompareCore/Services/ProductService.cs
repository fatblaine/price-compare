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

                // Push same_product lookup into SQL as a correlated subquery instead of loading
                // all GUIDs into Lambda memory and sending a large ANY(@ids) parameter each request.
                // PostgreSQL uses IX_ProductMatch_MatchType_SourceProductId and optimises to a hash
                // semi-join — no round-trip data transfer between DB and Lambda.
                var sameProductSubquery = _db.ProductMatches
                    .AsNoTracking()
                    .Where(m => m.MatchType == "same_product")
                    .Select(m => m.SourceProductId)
                    .Distinct();

                IQueryable<Product> orderedQuery;
                if (hasFilter)
                {
                    orderedQuery = query
                        .Select(p => new { Product = p, HasMatch = sameProductSubquery.Contains(p.ProductId) })
                        .OrderByDescending(x => x.HasMatch)
                        .ThenBy(x => x.Product.Name)
                        .ThenBy(x => x.Product.ProductId)
                        .Select(x => x.Product);
                }
                else
                {
                    orderedQuery = query
                        .Select(p => new { Product = p, HasMatch = sameProductSubquery.Contains(p.ProductId) })
                        .OrderByDescending(x => x.HasMatch)
                        .ThenBy(x => x.Product.ProductId)
                        .Select(x => x.Product);
                }

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
