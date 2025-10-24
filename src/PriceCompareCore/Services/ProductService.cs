using System;
using System.Collections.Generic;
using System.Linq;
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

                var total = await query.CountAsync();

                var items = await query
                    .OrderBy(p => p.Name)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return (total, items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying products");
                throw;
            }
        }
    }
}
