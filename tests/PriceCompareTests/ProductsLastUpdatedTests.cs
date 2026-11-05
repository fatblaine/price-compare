using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PriceCompareData.Common;
using PriceCompareData.Data;
using PriceCompareData.Entities.Compare;
using PriceCompareWeb.Controllers;
using PriceCompareWeb.Controllers.Models;

namespace PriceCompareTests
{
    /// <summary>
    /// GET /api/Products/last-updated: latest lastseenat overall and per shop, UTC, cached.
    /// </summary>
    public class ProductsLastUpdatedTests
    {
        private static AppDbContext CreateDb()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        private static IDistributedCache CreateCache() =>
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        private static ProductsController CreateController(AppDbContext db, IDistributedCache cache) =>
            new(productService: null!, NullLogger<ProductsController>.Instance, db, descriptionSearchService: null!, cache);

        private static void AddProduct(AppDbContext db, int? shopType, DateTime lastSeenAt)
        {
            db.Products.Add(new Product
            {
                ProductId = Guid.NewGuid(),
                Name = "p-" + Guid.NewGuid().ToString("N")[..6],
                ShopType = shopType,
                LastSeenAt = lastSeenAt
            });
        }

        private static async Task<LastUpdatedDto> Get(ProductsController controller)
        {
            var result = await controller.GetLastUpdated(CancellationToken.None);
            var ok = Assert.IsType<OkObjectResult>(result);
            return Assert.IsType<LastUpdatedDto>(ok.Value);
        }

        [Fact]
        public async Task Returns_latest_time_per_shop_and_overall()
        {
            using var db = CreateDb();
            var colesOld = new DateTime(2026, 10, 28, 1, 0, 0, DateTimeKind.Utc);
            var colesNew = new DateTime(2026, 10, 29, 2, 30, 0, DateTimeKind.Utc);
            var wwsNew = new DateTime(2026, 11, 3, 14, 5, 0, DateTimeKind.Utc);
            AddProduct(db, ShopType.COLES, colesOld);
            AddProduct(db, ShopType.COLES, colesNew);
            AddProduct(db, ShopType.WOOLWORTHS, wwsNew.AddDays(-7));
            AddProduct(db, ShopType.WOOLWORTHS, wwsNew);
            AddProduct(db, null, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)); // no shop: ignored
            await db.SaveChangesAsync();

            var dto = await Get(CreateController(db, CreateCache()));

            Assert.Equal(wwsNew, dto.LastUpdatedAt);
            Assert.Equal(new[] { ShopType.COLES, ShopType.WOOLWORTHS }, dto.Shops.Select(s => s.ShopType));
            Assert.Equal(new[] { "Coles", "Woolworths" }, dto.Shops.Select(s => s.ShopName));
            Assert.Equal(colesNew, dto.Shops[0].LastUpdatedAt);
            Assert.Equal(wwsNew, dto.Shops[1].LastUpdatedAt);
            Assert.All(dto.Shops, s => Assert.Equal(DateTimeKind.Utc, s.LastUpdatedAt!.Value.Kind));
        }

        [Fact]
        public async Task Shop_without_products_is_null()
        {
            using var db = CreateDb();
            var wws = new DateTime(2026, 11, 3, 14, 5, 0, DateTimeKind.Utc);
            AddProduct(db, ShopType.WOOLWORTHS, wws);
            await db.SaveChangesAsync();

            var dto = await Get(CreateController(db, CreateCache()));

            Assert.Equal(wws, dto.LastUpdatedAt);
            Assert.Null(dto.Shops.Single(s => s.ShopType == ShopType.COLES).LastUpdatedAt);
        }

        [Fact]
        public async Task Empty_database_returns_nulls()
        {
            using var db = CreateDb();

            var dto = await Get(CreateController(db, CreateCache()));

            Assert.Null(dto.LastUpdatedAt);
            Assert.Equal(2, dto.Shops.Count);
            Assert.All(dto.Shops, s => Assert.Null(s.LastUpdatedAt));
        }

        [Fact]
        public async Task Second_call_is_served_from_cache()
        {
            using var db = CreateDb();
            var first = new DateTime(2026, 11, 3, 14, 5, 0, DateTimeKind.Utc);
            AddProduct(db, ShopType.WOOLWORTHS, first);
            await db.SaveChangesAsync();
            var cache = CreateCache();

            var dto1 = await Get(CreateController(db, cache));

            // A newer import lands, but the cached answer is still returned within the TTL.
            AddProduct(db, ShopType.WOOLWORTHS, first.AddDays(1));
            await db.SaveChangesAsync();
            var dto2 = await Get(CreateController(db, cache));

            Assert.Equal(first, dto1.LastUpdatedAt);
            Assert.Equal(first, dto2.LastUpdatedAt);
            Assert.Equal(DateTimeKind.Utc, dto2.LastUpdatedAt!.Value.Kind);
        }
    }
}
