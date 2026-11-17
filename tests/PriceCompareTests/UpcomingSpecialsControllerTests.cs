using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PriceCompareData.Data;
using PriceCompareData.Entities.Catalogue;
using PriceCompareWeb.Controllers;
using PriceCompareWeb.Controllers.Models;

namespace PriceCompareTests
{
    /// <summary>BTS-163: POST /api/upcoming-specials/by-products.</summary>
    public class UpcomingSpecialsControllerTests
    {
        private static readonly DateOnly Tuesday = new(2026, 11, 17);
        private static readonly DateOnly NextWednesday = new(2026, 11, 18);

        private static AppDbContext CreateDb() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static IConfiguration Config(bool? enabled = null) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(enabled is null
                    ? new Dictionary<string, string?>()
                    : new Dictionary<string, string?> { ["UpcomingSpecials:Enabled"] = enabled.Value ? "true" : "false" })
                .Build();

        private static void AddItem(AppDbContext db, int saleId, string itemId, decimal price, DateOnly validFrom, params Guid?[] products)
        {
            db.CatalogueSpecials.Add(new CatalogueSpecial
            {
                ShopType = 1,
                SaleId = saleId,
                Region = "NSW Specialty",
                ItemId = itemId,
                Name = "Item " + itemId,
                Price = price,
                PriceUnit = "each",
                ValidFrom = validFrom,
                ValidTo = validFrom.AddDays(6),
                ScrapedAt = DateTime.UtcNow,
            });
            var n = 0;
            foreach (var product in products)
            {
                db.CatalogueSpecialSkus.Add(new CatalogueSpecialSku { ShopType = 1, SaleId = saleId, ItemId = itemId, Sku = $"{itemId}-{n++}", ProductId = product });
            }
        }

        private static Dictionary<string, UpcomingSpecialDto?> Results(IActionResult result)
        {
            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var prop = ok.Value!.GetType().GetProperty("results")!;
            return (Dictionary<string, UpcomingSpecialDto?>)prop.GetValue(ok.Value)!;
        }

        [Fact]
        public async Task ReturnsNextWeekPriceAndNullForOthers()
        {
            using var db = CreateDb();
            var coconut = Guid.NewGuid();
            var other = Guid.NewGuid();
            AddItem(db, 68980, "1", 2.75m, NextWednesday, coconut, null);
            await db.SaveChangesAsync();

            var controller = new UpcomingSpecialsController(db, Config(), () => Tuesday);
            var results = Results(await controller.ByProducts(new UpcomingSpecialsRequest { ProductIds = { coconut, other } }));

            results[coconut.ToString()]!.Price.Should().Be(2.75m);
            results[coconut.ToString()]!.ValidFrom.Should().Be(NextWednesday);
            results[other.ToString()].Should().BeNull();
        }

        [Fact]
        public async Task CatalogueThatHasStarted_IsNotReturned()
        {
            using var db = CreateDb();
            var product = Guid.NewGuid();
            AddItem(db, 68980, "1", 2.75m, NextWednesday, product);
            await db.SaveChangesAsync();

            var controller = new UpcomingSpecialsController(db, Config(), () => NextWednesday);
            var results = Results(await controller.ByProducts(new UpcomingSpecialsRequest { ProductIds = { product } }));

            results[product.ToString()].Should().BeNull("from Wednesday the scraped price takes over");
        }

        [Fact]
        public async Task ProductInTwoItems_GetsTheLowerPrice()
        {
            using var db = CreateDb();
            var product = Guid.NewGuid();
            AddItem(db, 68980, "1", 5.00m, NextWednesday, product);
            AddItem(db, 68980, "2", 4.00m, NextWednesday, product);
            await db.SaveChangesAsync();

            var controller = new UpcomingSpecialsController(db, Config(), () => Tuesday);
            var results = Results(await controller.ByProducts(new UpcomingSpecialsRequest { ProductIds = { product } }));

            results[product.ToString()]!.Price.Should().Be(4.00m);
        }

        [Fact]
        public async Task KillSwitch_ReturnsNullForEveryProduct()
        {
            using var db = CreateDb();
            var product = Guid.NewGuid();
            AddItem(db, 68980, "1", 2.75m, NextWednesday, product);
            await db.SaveChangesAsync();

            var controller = new UpcomingSpecialsController(db, Config(enabled: false), () => Tuesday);
            var results = Results(await controller.ByProducts(new UpcomingSpecialsRequest { ProductIds = { product } }));

            results[product.ToString()].Should().BeNull();
        }

        // ASP.NET creates controllers through ActivatorUtilities; two usable constructors made every
        // request fail with 500 ("Multiple constructors accepting all given argument types").
        [Fact]
        public void CanBeCreatedTheWayAspNetCreatesControllers()
        {
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddSingleton(Config());
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            // Same call MVC's controller activator makes (no explicit arguments).
            var act = () => ActivatorUtilities
                .CreateFactory(typeof(UpcomingSpecialsController), Type.EmptyTypes)
                .Invoke(scope.ServiceProvider, null);

            act.Should().NotThrow();
            act().Should().BeOfType<UpcomingSpecialsController>();
        }

        [Fact]
        public async Task RejectsEmptyAndOversizedBatches()
        {
            using var db = CreateDb();
            var controller = new UpcomingSpecialsController(db, Config(), () => Tuesday);

            (await controller.ByProducts(new UpcomingSpecialsRequest())).Should().BeOfType<BadRequestObjectResult>();

            var tooMany = new UpcomingSpecialsRequest { ProductIds = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList() };
            (await controller.ByProducts(tooMany)).Should().BeOfType<BadRequestObjectResult>();
        }
    }
}
