using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        public IReadOnlyList<Product> MapColesSpecialProducts(IEnumerable<ColesSpecialProduct> items)
            => items.Select(MapColesSpecial).ToList();

        public IReadOnlyList<Product> MapColesDownProducts(IEnumerable<ColesDownProduct> items)
            => items.Select(MapColesDown).ToList();

        public IReadOnlyList<Product> MapWoolworthsProducts(IEnumerable<WoolworthsSpecialProduct> items)
            => items.Select(MapWoolworthsSpecial).ToList();

        // Map ColesSpecialProduct to Product
        private Product MapColesSpecial(ColesSpecialProduct p)
        {
            var name = p.Name ?? string.Empty;
            var (sv, su, pk) = _mapper.ParseSpec(name);
            var catId = _mapper.MapCategoryId(name);
            var product = new Product
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
            ApplyStandardFields(product);
            return product;
        }

        private Product MapColesDown(ColesDownProduct p)
        {
            var name = p.Name ?? string.Empty;
            var (sv, su, pk) = _mapper.ParseSpec(name);
            var catId = _mapper.MapCategoryId(name);
            var product = new Product
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
            ApplyStandardFields(product);
            return product;
        }

        private Product MapWoolworthsSpecial(WoolworthsSpecialProduct p)
        {
            var name = p.DisplayName ?? string.Empty;
            var (sv, su, pk) = _mapper.ParseSpec(name);
            var catId = _mapper.MapCategoryId(name, p.Brand);

            // Prefer stable upstream IDs. Lower-shelf DOM scraping may have empty barcode and stockcode=0.
            // In that case keep SourceId null so import uses name-based branch instead of collapsing to "0".
            string? sourceId = null;
            if (!string.IsNullOrWhiteSpace(p.Barcode))
            {
                sourceId = p.Barcode.Trim();
            }
            else if (p.Stockcode > 0)
            {
                sourceId = p.Stockcode.ToString();
            }

            var product = new Product
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
            ApplyStandardFields(product);
            return product;
        }

        // Upsert products into database
        private async Task UpsertAsync(IEnumerable<Product> candidates)
        {
            var list = candidates.ToList();
            if (list.Count == 0) return;

            foreach (var c in list) ApplyStandardFields(c);

            var shopTypes = list.Select(c => c.ShopType).Distinct().ToList();

            // Load only the existing products this batch could possibly match, keyed the same way
            // the lookups below are (SourceId first, Name as fallback).
            //
            // This previously loaded EVERY product for the shop type on every scraper run. With ~26
            // weekly scrapers each pulling the whole shop catalogue into memory (and into the EF
            // change tracker), that was the single largest source of Supabase read pressure and of
            // scraper runtime. A batch is typically a few hundred to a couple of thousand rows, so
            // scoping the query is an order-of-magnitude reduction. Npgsql translates these
            // Contains() calls to `= ANY(@p)` (one array parameter each), not a giant IN list.
            var batchSourceIds = list
                .Where(c => !string.IsNullOrWhiteSpace(c.SourceId))
                .Select(c => c.SourceId!)
                .Distinct()
                .ToList();

            var batchNames = list
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .Select(c => c.Name!)
                .Distinct()
                .ToList();

            var existingProducts = await _dbContext.Products
                .Where(p => shopTypes.Contains(p.ShopType)
                            && ((p.SourceId != null && batchSourceIds.Contains(p.SourceId))
                                || (p.Name != null && batchNames.Contains(p.Name))))
                .ToListAsync();

            // Build O(1) lookups: SourceId-first, Name-fallback
            var bySourceId = new Dictionary<(int? ShopType, string SourceId), Product>();
            var byName = new Dictionary<(int? ShopType, string Name), Product>();
            foreach (var p in existingProducts)
            {
                if (!string.IsNullOrWhiteSpace(p.SourceId))
                    bySourceId.TryAdd((p.ShopType, p.SourceId!), p);
                if (!string.IsNullOrWhiteSpace(p.Name))
                    byName.TryAdd((p.ShopType, p.Name!), p);
            }

            foreach (var c in list)
            {
                Product? existing = null;

                if (!string.IsNullOrWhiteSpace(c.SourceId))
                    bySourceId.TryGetValue((c.ShopType, c.SourceId!), out existing);

                if (existing is null && !string.IsNullOrWhiteSpace(c.Name))
                    byName.TryGetValue((c.ShopType, c.Name!), out existing);

                if (existing is null)
                {
                    await _dbContext.Products.AddAsync(c);
                    // Track newly added products to handle duplicates within the same batch
                    if (!string.IsNullOrWhiteSpace(c.SourceId))
                        bySourceId.TryAdd((c.ShopType, c.SourceId!), c);
                    if (!string.IsNullOrWhiteSpace(c.Name))
                        byName.TryAdd((c.ShopType, c.Name!), c);
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
                    ApplyStandardFields(existing);
                    existing.LastSeenAt = DateTime.UtcNow;
                }
            }

            await _dbContext.SaveChangesAsync();
        }

        private static void ApplyStandardFields(Product product)
        {
            product.NormalizedName = NormalizeText(product.Name);
            product.NormalizedBrand = NormalizeText(product.Brand);

            var (stdValue, stdUnit, sizeUnknown) = StandardizeSize(product.SizeValue, product.SizeUnit);
            product.SizeStandardValue = stdValue;
            product.SizeStandardUnit = stdUnit;
            product.SizeUnknown = sizeUnknown;

            product.BrandUnknown = string.IsNullOrWhiteSpace(product.NormalizedBrand);
            product.CategoryUnknown = !product.CategoryId.HasValue || product.CategoryId.Value <= 0;
        }

        private static string? NormalizeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var sb = new StringBuilder(value.Length);
            var lastWasSpace = false;
            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(char.ToLowerInvariant(ch));
                    lastWasSpace = false;
                }
                else if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }

            var normalized = sb.ToString().Trim();
            return normalized.Length == 0 ? null : normalized;
        }

        private static (decimal? value, string? unit, bool unknown) StandardizeSize(decimal? sizeValue, string? sizeUnit)
        {
            if (!sizeValue.HasValue || sizeValue.Value <= 0m || string.IsNullOrWhiteSpace(sizeUnit))
            {
                return (null, null, true);
            }

            var unit = sizeUnit.Trim().ToLowerInvariant();
            switch (unit)
            {
                case "g":
                    return (sizeValue.Value, "g", false);
                case "kg":
                    return (sizeValue.Value * 1000m, "g", false);
                case "mg":
                    return (sizeValue.Value / 1000m, "g", false);
                case "ml":
                    return (sizeValue.Value, "ml", false);
                case "l":
                case "lt":
                case "ltr":
                case "liter":
                case "litre":
                case "liters":
                case "litres":
                    return (sizeValue.Value * 1000m, "ml", false);
                default:
                    return (null, null, true);
            }
        }
    }
}
