using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PriceCompareCore.Jobs;
using PriceCompareCore.Services.Catalogue;
using PriceCompareData.Data;

namespace PriceCompareTests
{
    /// <summary>BTS-163: choosing the sale, collecting SKUs and failing loudly.</summary>
    public class CatalogueFetchServiceTests
    {
        private static readonly DateOnly Monday = new(2026, 11, 16);

        private sealed class FakeClient : ISaleFinderClient
        {
            public List<SaleFinderSale> Sales { get; } = new();
            public string ListHtml { get; set; } = string.Empty;
            public Dictionary<string, IReadOnlyList<string>> Skus { get; } = new();
            public HashSet<string> FailingItems { get; } = new();

            public Task<IReadOnlyList<SaleFinderSale>> GetSalesAsync(int shopType, CatalogueRetailerOptions retailer, CancellationToken ct) =>
                Task.FromResult<IReadOnlyList<SaleFinderSale>>(Sales);

            public Task<SaleFinderItemList> GetItemListAsync(int saleId, CatalogueRetailerOptions retailer, CancellationToken ct) =>
                Task.FromResult(new SaleFinderItemList("NSW Specialty", "Weekly Catalogue NSW", new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 24), ListHtml));

            public Task<IReadOnlyList<string>> GetItemSkusAsync(string itemId, CatalogueRetailerOptions retailer, CancellationToken ct)
            {
                if (FailingItems.Contains(itemId))
                {
                    throw new HttpRequestException("boom");
                }

                return Task.FromResult(Skus.TryGetValue(itemId, out var s) ? s : (IReadOnlyList<string>)Array.Empty<string>());
            }
        }

        private sealed class CapturingExecutor : ICatalogueSqlExecutor
        {
            public List<string> Scripts { get; } = new();

            public Task ExecuteAsync(string sql, CancellationToken ct)
            {
                Scripts.Add(sql);
                return Task.CompletedTask;
            }
        }

        private static string ItemHtml(string id, string name, string price, string? stockcode = null) =>
            $@"<div class=""onesite-base shelfProductStamp"" data-stockcode=""{stockcode}"">
<span class=""sf-item-heading"" data-itemid=""{id}"">{name}</span>
<div class=""sf-price-options""><span class=""sf-nowprice""><span class=""sf-pricedisplay"">${price}</span><span class=""sf-optionsuffix"">each</span></span></div></div>";

        private static (CatalogueFetchService Service, FakeClient Client, CapturingExecutor Executor) Create()
        {
            var client = new FakeClient();
            client.Sales.Add(new SaleFinderSale(68850, "Weekly Catalogue NSW", new DateTime(2026, 11, 9, 17, 0, 0), new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 17)));
            client.Sales.Add(new SaleFinderSale(68943, "Health & Beauty Catalogue NSW", new DateTime(2026, 11, 16, 17, 0, 0), new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 24)));
            client.Sales.Add(new SaleFinderSale(68980, "Weekly Catalogue NSW", new DateTime(2026, 11, 16, 17, 0, 0), new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 24)));

            var executor = new CapturingExecutor();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var service = new CatalogueFetchService(
                client, executor, db, Options.Create(new CatalogueOptions { ItemDelayMs = 0 }),
                NullLogger<CatalogueFetchService>.Instance, () => Monday, (_, _) => Task.CompletedTask);
            return (service, client, executor);
        }

        [Fact]
        public async Task StoresNextWeeklyCatalogueWithDetailSkusAndListFallback()
        {
            var (service, client, executor) = Create();
            client.ListHtml = ItemHtml("1", "Cocobella Coconut Water 1 Litre", "2.75", "724514")
                              + ItemHtml("2", "Australian R2E2 Mangoes", "3.00", "143168");
            client.Skus["1"] = new[] { "724514", "500999" };   // detail lists every flavour
                                                              // item 2: detail has none → list stockcode is used

            var result = await service.RunAsync(1, failIfNotPublished: false);

            result.Skipped.Should().BeFalse();
            result.SaleId.Should().Be(68980, "Health & Beauty is skipped and last week's sale has started");
            result.Items.Should().Be(2);
            result.ItemsWithSku.Should().Be(2);
            result.Skus.Should().Be(3);
            result.Region.Should().Be("NSW Specialty");

            var sql = executor.Scripts.Single();
            sql.Should().Contain("(1, 68980, '1', '500999')");
            sql.Should().Contain("(1, 68980, '2', '143168')");
        }

        [Fact]
        public async Task NotPublishedYet_IsSkippedOnMondayAndFailsOnTuesday()
        {
            var (service, client, executor) = Create();
            client.Sales.RemoveAll(s => s.SaleId == 68980);

            var skipped = await service.RunAsync(1, failIfNotPublished: false);
            skipped.Skipped.Should().BeTrue();
            executor.Scripts.Should().BeEmpty();

            var act = () => service.RunAsync(1, failIfNotPublished: true);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No catalogue starting after*");
        }

        [Fact]
        public async Task ZeroParsedItems_Fails()
        {
            var (service, client, executor) = Create();
            client.ListHtml = "<div>nothing here</div>";

            var act = () => service.RunAsync(1, failIfNotPublished: false);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*parsed 0 items*");
            executor.Scripts.Should().BeEmpty();
        }

        [Fact]
        public async Task MostItemsWithoutSkus_Fails()
        {
            var (service, client, executor) = Create();
            client.ListHtml = ItemHtml("1", "A", "1.00") + ItemHtml("2", "B", "2.00") + ItemHtml("3", "C", "3.00");
            client.Skus["1"] = new[] { "11" };

            var act = () => service.RunAsync(1, failIfNotPublished: false);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*have no product codes*");
            executor.Scripts.Should().BeEmpty();
        }

        [Fact]
        public async Task ManyFailedDetailRequests_Fails()
        {
            var (service, client, executor) = Create();
            client.ListHtml = ItemHtml("1", "A", "1.00", "11") + ItemHtml("2", "B", "2.00", "22");
            client.FailingItems.Add("2");

            var act = () => service.RunAsync(1, failIfNotPublished: false);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*item-detail requests failed*");
        }

        [Theory]
        [InlineData(2026, 11, 16, false)]   // Monday
        [InlineData(2026, 11, 17, true)]    // Tuesday
        [InlineData(2026, 11, 18, false)]   // Wednesday
        public void FailIfNotPublished_OnlyOnTuesday(int y, int m, int d, bool expected)
        {
            CatalogueFetchJob.FailIfNotPublished(new DateOnly(y, m, d)).Should().Be(expected);
        }
    }
}
