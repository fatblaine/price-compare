using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PriceCompareCore.Interfaces;
using PriceCompareData.Common;
using PriceCompareData.Data;
using PriceCompareData.Entities;
using PriceCompareData.Entities.Compare;
using PriceCompareData.Entities.Scraping;

namespace PriceCompareCore.Services
{
    public class IngestionService : IIngestionService
    {
        private readonly AppDbContext _dbContext;
        private readonly ICategoryMappingService _mapper;

        public IngestionService(AppDbContext dbContext, ICategoryMappingService mapper)
        {
            _dbContext = dbContext;
            _mapper = mapper;
        }

        // Upsert products from Coles Specials
        public async Task UpsertColesSpecialAsync(IEnumerable<ColesSpecialProduct> items)
            => await UpsertAsync(items.Select(MapColesSpecial));

        // Upsert products from Coles down-down
        public async Task UpsertColesDownAsync(IEnumerable<ColesDownProduct> items)
        {
            await UpsertAsync(items.Select(MapColesDown));
        }

        // Upsert products from Woolworths Specials
        public async Task UpsertWwsAsync(IEnumerable<WoolworthsSpecialProduct> items)
        {
            await UpsertAsync(items.Select(MapWoolworthsSpecial));
        }

        // Map ColesSpecialProduct to Product
        private Product MapColesSpecial(ColesSpecialProduct p)
        {
            var name = p.Name ?? string.Empty;
            var (sv, su, pk) = _mapper.ParseSpec(name);
            var catId = _mapper.MapCategoryId(name);
            return new Product
            {
                ShopType = ShopType.COLES,
                SourceId = p.Id > 0 ? p.Id.ToString() : null,
                Name = name,
                Brand = null,
                SizeValue = sv,
                SizeUnit = su,
                PackageQty = pk,
                CategoryId = catId,
                ImageUrl = p.ImageUrl,
                LastSeenAt = DateTime.UtcNow
            };
        }

        private Product MapColesDown(ColesDownProduct p)
        {
            var name = p.Name ?? string.Empty;
            var (sv, su, pk) = _mapper.ParseSpec(name);
            var catId = _mapper.MapCategoryId(name);
            return new Product
            {
                ShopType = ShopType.COLES,
                SourceId = null,
                Name = name,
                Brand = null,
                SizeValue = sv,
                SizeUnit = su,
                PackageQty = pk,
                CategoryId = catId,
                ImageUrl = p.ImageUrl,
                LastSeenAt = DateTime.UtcNow
            };
        }

        private Product MapWoolworthsSpecial(WoolworthsSpecialProduct p)
        {
            var name = p.DisplayName ?? string.Empty;
            var (sv, su, pk) = _mapper.ParseSpec(name);
            var catId = _mapper.MapCategoryId(name, p.Brand);

            var sourceId = !string.IsNullOrWhiteSpace(p.Barcode)
                ? p.Barcode
                : p.Stockcode.ToString();

            return new Product
            {
                ShopType = ShopType.WOOLWORTHS,
                SourceId = sourceId,
                Name = name,
                Brand = string.IsNullOrWhiteSpace(p.Brand) ? null : p.Brand,
                SizeValue = sv,
                SizeUnit = su,
                PackageQty = pk,
                CategoryId = catId,
                ImageUrl = p.LargeImageFile,
                LastSeenAt = DateTime.UtcNow
            };
        }

        // Upsert products into database
        private async Task UpsertAsync(IEnumerable<Product> candidates)
        {
            foreach (var c in candidates)
            {
                Product? existing = null;

                // try to find existing by (ShopType + SourceId)
                if (!string.IsNullOrWhiteSpace(c.SourceId))
                {
                    existing = await _dbContext.Products
                        .FirstOrDefaultAsync(x => x.ShopType == c.ShopType && x.SourceId == c.SourceId);
                }

                // try to find existing by (ShopType + Name)
                if (existing is null)
                {
                    existing = await _dbContext.Products
                        .FirstOrDefaultAsync(x => x.ShopType == c.ShopType && x.Name == c.Name);
                }

                if (existing is null)
                {
                    await _dbContext.Products.AddAsync(c);
                }
                else
                {
                    existing.Name = c.Name;
                    existing.Brand = c.Brand ?? existing.Brand;
                    existing.SizeValue = c.SizeValue ?? existing.SizeValue;
                    existing.SizeUnit = c.SizeUnit ?? existing.SizeUnit;
                    existing.PackageQty = c.PackageQty ?? existing.PackageQty;
                    existing.CategoryId = c.CategoryId ?? existing.CategoryId;
                    existing.ImageUrl = c.ImageUrl ?? existing.ImageUrl;
                    existing.LastSeenAt = DateTime.UtcNow;
                }
            }

            await _dbContext.SaveChangesAsync();
        }
    }
}