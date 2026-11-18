using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PriceCompareCore.Services;
using PriceCompareData.Common;
using PriceCompareData.Data;
using PriceCompareData.Entities.Compare;

namespace PriceCompareTests
{
    /// <summary>
    /// GET /api/Products ordering: with no filter, products sold by both shops (HasCrossShopMatch)
    /// come first, then ProductId; with a filter, ordering stays Name, ProductId.
    /// </summary>
    public class ProductServiceOrderingTests
    {
        private static readonly DateTime Now = new(2026, 11, 18, 2, 0, 0, DateTimeKind.Utc);

        private static AppDbContext CreateDb()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        private static Product AddProduct(AppDbContext db, string id, string name, bool matched, int shopType = ShopType.COLES)
        {
            var product = new Product
            {
                ProductId = Guid.Parse(id),
                Name = name,
                ShopType = shopType,
                LastSeenAt = Now,
                HasCrossShopMatch = matched
            };
            db.Products.Add(product);
            return product;
        }

        private static async Task<ProductService> SeedAsync(AppDbContext db)
        {
            AddProduct(db, "00000000-0000-0000-0000-000000000001", "Apple", matched: false);
            AddProduct(db, "00000000-0000-0000-0000-000000000002", "Zucchini", matched: true);
            AddProduct(db, "00000000-0000-0000-0000-000000000003", "Banana", matched: false, ShopType.WOOLWORTHS);
            AddProduct(db, "00000000-0000-0000-0000-000000000004", "Milk", matched: true, ShopType.WOOLWORTHS);
            await db.SaveChangesAsync();
            return new ProductService(db, NullLogger<ProductService>.Instance);
        }

        [Fact]
        public async Task Unfiltered_list_puts_cross_shop_products_first_then_product_id()
        {
            using var db = CreateDb();
            var service = await SeedAsync(db);

            var (total, items) = await service.GetProductsAsync(1, 20);

            Assert.Equal(4, total);
            Assert.Equal(new[] { "Zucchini", "Milk", "Apple", "Banana" }, items.Select(p => p.Name));
        }

        [Fact]
        public async Task Cross_shop_products_fill_the_first_page()
        {
            using var db = CreateDb();
            var service = await SeedAsync(db);

            var (_, page1) = await service.GetProductsAsync(1, 2);

            Assert.All(page1, p => Assert.True(p.HasCrossShopMatch));
        }

        [Fact]
        public async Task Filtered_list_keeps_name_order()
        {
            using var db = CreateDb();
            var service = await SeedAsync(db);

            var (_, items) = await service.GetProductsAsync(1, 20, shopType: ShopType.WOOLWORTHS);

            Assert.Equal(new[] { "Banana", "Milk" }, items.Select(p => p.Name));
        }
    }
}
