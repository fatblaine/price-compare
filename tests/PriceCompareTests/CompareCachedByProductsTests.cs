using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceCompareData.Data;
using PriceCompareData.DTOs;
using PriceCompareData.Entities.Compare;
using PriceCompareData.Entities.History;
using PriceCompareWeb.Controllers;
using PriceCompareWeb.Controllers.Models;

namespace PriceCompareTests
{
    /// <summary>
    /// BTS-153: POST /api/compare-cached/by-products must pick exactly what the product cards used
    /// to pick from GET /api/compare-cached/by-product — the first same_product candidate with a
    /// price, forward matches first, reverse only when there is no forward match at all.
    /// </summary>
    public class CompareCachedByProductsTests
    {
        private static AppDbContext CreateDb()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        private static Product AddProduct(AppDbContext db, string name, int shopType, decimal? price)
        {
            var product = new Product
            {
                ProductId = Guid.NewGuid(),
                Name = name,
                ShopType = shopType,
                SizeValue = 2m,
                LastSeenAt = DateTime.UtcNow
            };
            db.Products.Add(product);

            if (price.HasValue)
            {
                db.PriceHistory.Add(new PriceHistory
                {
                    Name = name,
                    ShopType = shopType,
                    CurrentPrice = price.Value,
                    ImageUrl = "http://example.com/i.png",
                    ScrapedAt = DateTime.UtcNow
                });
            }

            return product;
        }

        private static void AddMatch(AppDbContext db, Product source, Product target, decimal score, string matchType)
        {
            db.ProductMatches.Add(new ProductMatch
            {
                Id = Guid.NewGuid(),
                SourceProductId = source.ProductId,
                TargetProductId = target.ProductId,
                SourceShopType = source.ShopType ?? 0,
                TargetShopType = target.ShopType ?? 0,
                Score = score,
                Method = "exact",
                MatchType = matchType,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        private static async Task<Dictionary<string, ProductMatchCandidate?>> PostAsync(
            AppDbContext db, params Guid[] ids)
        {
            var controller = new CompareCachedController(db);
            var response = await controller.CompareCachedByProducts(
                new BatchCompareRequest { SourceProductIds = ids.ToList() });

            var ok = Assert.IsType<OkObjectResult>(response);
            var results = ok.Value!.GetType().GetProperty("results")!.GetValue(ok.Value);
            return Assert.IsType<Dictionary<string, ProductMatchCandidate?>>(results);
        }

        [Fact]
        public async Task ReturnsBestSameProductMatch_ForForwardDirection()
        {
            using var db = CreateDb();
            var source = AddProduct(db, "Milk 2L", 0, 3.50m);
            var weak = AddProduct(db, "Milk 2L Woolworths comparable", 1, 4.00m);
            var best = AddProduct(db, "Milk 2L Woolworths", 1, 3.20m);
            AddMatch(db, source, weak, 0.60m, "comparable");
            AddMatch(db, source, best, 0.97m, "same_product");
            await db.SaveChangesAsync();

            var results = await PostAsync(db, source.ProductId);

            var candidate = results[source.ProductId.ToString()];
            Assert.NotNull(candidate);
            Assert.Equal(best.ProductId, candidate!.Target.ProductId);
            Assert.Equal("same_product", candidate.MatchType);
            Assert.Equal(3.20m, candidate.LatestPrice);
            Assert.Equal(1.60m, candidate.PricePerUnit); // 3.20 / sizeValue 2
        }

        [Fact]
        public async Task FallsBackToReverseDirection_WhenNoForwardMatchExists()
        {
            using var db = CreateDb();
            var source = AddProduct(db, "Bread 700g", 1, 4.50m);
            var other = AddProduct(db, "Bread 700g Coles", 0, 4.00m);
            AddMatch(db, other, source, 0.95m, "same_product"); // source is the TARGET here
            await db.SaveChangesAsync();

            var results = await PostAsync(db, source.ProductId);

            var candidate = results[source.ProductId.ToString()];
            Assert.NotNull(candidate);
            Assert.Equal(other.ProductId, candidate!.Target.ProductId);
            Assert.Equal(4.00m, candidate.LatestPrice);
        }

        [Fact]
        public async Task DoesNotReverseLookup_WhenForwardMatchExistsButIsOnlyComparable()
        {
            using var db = CreateDb();
            var source = AddProduct(db, "Rice 1kg", 0, 5.00m);
            var comparable = AddProduct(db, "Rice 1kg other brand", 1, 4.80m);
            var reverseSame = AddProduct(db, "Rice 1kg same", 1, 4.50m);
            AddMatch(db, source, comparable, 0.60m, "comparable");
            AddMatch(db, reverseSame, source, 0.99m, "same_product");
            await db.SaveChangesAsync();

            var results = await PostAsync(db, source.ProductId);

            Assert.Null(results[source.ProductId.ToString()]);
        }

        [Fact]
        public async Task SkipsSameProductMatchesWithoutPrice()
        {
            using var db = CreateDb();
            var source = AddProduct(db, "Eggs 12pk", 0, 6.00m);
            var noPrice = AddProduct(db, "Eggs 12pk no price", 1, null);
            var withPrice = AddProduct(db, "Eggs 12pk priced", 1, 5.50m);
            AddMatch(db, source, noPrice, 0.99m, "same_product");
            AddMatch(db, source, withPrice, 0.93m, "same_product");
            await db.SaveChangesAsync();

            var results = await PostAsync(db, source.ProductId);

            var candidate = results[source.ProductId.ToString()];
            Assert.NotNull(candidate);
            Assert.Equal(withPrice.ProductId, candidate!.Target.ProductId);
        }

        [Fact]
        public async Task ReturnsNullEntry_ForProductWithoutAnyMatch()
        {
            using var db = CreateDb();
            var source = AddProduct(db, "Unmatched item", 0, 1.00m);
            await db.SaveChangesAsync();

            var results = await PostAsync(db, source.ProductId);

            Assert.True(results.ContainsKey(source.ProductId.ToString()));
            Assert.Null(results[source.ProductId.ToString()]);
        }

        [Fact]
        public async Task MatchesByProductResult_ForEveryProductOnAPage()
        {
            using var db = CreateDb();
            var sources = new List<Product>();
            for (var i = 0; i < 20; i++)
            {
                var source = AddProduct(db, $"Item {i}", 0, 10m + i);
                sources.Add(source);

                if (i % 3 == 0)
                {
                    AddMatch(db, source, AddProduct(db, $"Item {i} WW", 1, 9m + i), 0.95m, "same_product");
                }
                else if (i % 3 == 1)
                {
                    AddMatch(db, source, AddProduct(db, $"Item {i} WW alt", 1, 9m + i), 0.60m, "comparable");
                }
            }
            await db.SaveChangesAsync();

            var controller = new CompareCachedController(db);
            var batch = await PostAsync(db, sources.Select(s => s.ProductId).ToArray());

            foreach (var source in sources)
            {
                // What by-product + the old client-side rule would have produced.
                var single = await controller.CompareCachedByProduct(source.ProductId);
                var singleResult = (ProductMatchListResult)Assert.IsType<OkObjectResult>(single).Value!;
                var expected = singleResult.Matches
                    .FirstOrDefault(m => m.MatchType == "same_product" && m.LatestPrice != null);

                var actual = batch[source.ProductId.ToString()];
                Assert.Equal(expected?.Target.ProductId, actual?.Target.ProductId);
                Assert.Equal(expected?.LatestPrice, actual?.LatestPrice);
                Assert.Equal(expected?.Score, actual?.Score);
            }
        }

        [Fact]
        public async Task RejectsEmptyAndOversizedRequests()
        {
            using var db = CreateDb();
            var controller = new CompareCachedController(db);

            Assert.IsType<BadRequestObjectResult>(
                await controller.CompareCachedByProducts(new BatchCompareRequest()));

            Assert.IsType<BadRequestObjectResult>(
                await controller.CompareCachedByProducts(new BatchCompareRequest
                {
                    SourceProductIds = new List<Guid> { Guid.Empty }
                }));

            Assert.IsType<BadRequestObjectResult>(
                await controller.CompareCachedByProducts(new BatchCompareRequest
                {
                    SourceProductIds = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList()
                }));
        }
    }
}
