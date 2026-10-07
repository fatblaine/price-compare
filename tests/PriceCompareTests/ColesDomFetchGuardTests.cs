using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PriceCompareCore.Exceptions;
using PriceCompareCore.Interfaces;
using PriceCompareCore.Services;
using PriceCompareData.Common;
using PriceCompareData.Data;
using Quartz;

namespace PriceCompareTests
{
    // BTS-156: Coles category scrapes must fail loudly instead of "succeeding" with 0 products.
    public class ColesDomFetchGuardTests
    {
        private const string Url = "https://www.coles.com.au/_next/data/test/en/browse/bakery.json?slug=bakery";
        private const string ImpervaPage =
            "<!DOCTYPE html><html><head><title>Pardon Our Interruption</title></head>" +
            "<body><script src=\"/_Incapsula_Resource?x=1\"></script></body></html>";

        private static ColesBlockBreaker NewBreaker() => new(TimeSpan.FromMinutes(180));

        [Fact]
        public void Validate_ReturnsBody_WhenResponseIsJson()
        {
            ColesDomFetchGuard.Validate(200, "application/json", "{\"pageProps\":{}}", Url, NewBreaker())
                .Should().Be("{\"pageProps\":{}}");
        }

        [Fact]
        public void Validate_ThrowsBlockedAndTripsBreaker_WhenImpervaPageReturnedWith200()
        {
            var breaker = NewBreaker();

            var act = () => ColesDomFetchGuard.Validate(200, "text/html", ImpervaPage, Url, breaker);

            act.Should().Throw<ColesBlockedException>().Which.Message.Should().Contain("Imperva");
            breaker.BlockedUntilUtc.Should().NotBeNull();
        }

        [Fact]
        public void Breaker_Throws_WhileOpen()
        {
            var breaker = NewBreaker();
            breaker.Trip();

            var act = () => breaker.ThrowIfOpen(Url);

            act.Should().Throw<ColesBlockedException>().Which.Message.Should().Contain("cooling down");
        }

        [Fact]
        public void Breaker_Closes_AfterCooldown()
        {
            var now = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
            var breaker = new ColesBlockBreaker(TimeSpan.FromMinutes(180), () => now);
            breaker.Trip();

            now = now.AddMinutes(181);

            breaker.Invoking(b => b.ThrowIfOpen(Url)).Should().NotThrow();
            breaker.BlockedUntilUtc.Should().BeNull();
        }

        [Fact]
        public void Breaker_StaysClosed_WhenCooldownIsZero()
        {
            var breaker = new ColesBlockBreaker(TimeSpan.Zero);
            breaker.Trip();

            breaker.Invoking(b => b.ThrowIfOpen(Url)).Should().NotThrow();
        }

        [Theory]
        [InlineData(500, true)]
        [InlineData(504, true)]
        [InlineData(408, true)]
        [InlineData(404, false)]
        public void Validate_Throws_WhenStatusIsNotSuccess(int status, bool transient)
        {
            var breaker = NewBreaker();

            var act = () => ColesDomFetchGuard.Validate(status, "text/html", "<html>error</html>", Url, breaker);

            var ex = act.Should().Throw<ColesScrapeException>().Which;
            ex.Message.Should().Contain($"HTTP {status}");
            ex.IsTransient.Should().Be(transient);
            breaker.BlockedUntilUtc.Should().BeNull("only the Imperva page opens the breaker");
        }

        [Fact]
        public void Validate_Throws_WhenBodyIsNotJson()
        {
            var act = () => ColesDomFetchGuard.Validate(200, "text/html", "<html>maintenance</html>", Url, NewBreaker());

            act.Should().Throw<ColesScrapeException>().Which.Message.Should().Contain("non-JSON");
        }

        [Fact]
        public void EnsureProducts_Throws_WhenNothingWasExtracted()
        {
            var act = () => ColesDomFetchGuard.EnsureProducts(0, Url);
            act.Should().Throw<ColesScrapeException>().WithMessage("*0 products*");
        }
    }

    // BTS-156 P3: one scraper for every Coles category, fed by a page source (a browser in production).
    public class ColesCategoryScraperServiceTests
    {
        private static readonly ColesCategory Bakery = ColesCategories.FindBySlug("bakery")!;

        private static string PageJson(int noOfResults, int pageSize, params (int Id, string Name, decimal Price)[] products)
        {
            var results = string.Join(",", products.Select(p =>
                $"{{\"_type\":\"PRODUCT\",\"id\":{p.Id},\"name\":\"{p.Name}\",\"brand\":\"Coles\",\"size\":\"1 each\",\"pricing\":{{\"now\":{p.Price}}}}}"));
            return $"{{\"pageProps\":{{\"searchResults\":{{\"noOfResults\":{noOfResults},\"pageSize\":{pageSize},\"results\":[{results}]}}}}}}";
        }

        private static (ColesCategoryScraperService Scraper, AppDbContext Db, Mock<IScrapeExportService> Export) NewScraper(FakePageSource source)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
            var export = new Mock<IScrapeExportService>();
            var scraper = new ColesCategoryScraperService(
                source,
                new Mock<ILogger<ColesCategoryScraperService>>().Object,
                db,
                new Mock<IIngestionService>().Object,
                export.Object);
            return (scraper, db, export);
        }

        [Fact]
        public async Task ScrapeAsync_SavesEveryPage_AndStopsAtTheLastPage()
        {
            var source = new FakePageSource(
                PageJson(3, 2, (1, "Bread", 3.5m), (2, "Rolls", 4m)),
                PageJson(3, 2, (3, "Bagels", 5m)));
            var (scraper, db, export) = NewScraper(source);

            var products = await scraper.ScrapeAsync(Bakery);

            products.Should().HaveCount(3);
            source.RequestedPages.Should().Equal(1, 2);
            db.PriceHistory.Should().HaveCount(3).And.OnlyContain(ph => ph.OfferType == OfferType.BAKERY && ph.ShopType == ShopType.COLES);
            export.Verify(e => e.ExportAsync(It.Is<ScrapeExportRequest>(r => r.Source == "coles_bakery_json"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ScrapeAsync_Throws_WhenFirstPageHasNoProducts()
        {
            var (scraper, db, export) = NewScraper(new FakePageSource(PageJson(0, 48)));

            var act = () => scraper.ScrapeAsync(Bakery);

            await act.Should().ThrowAsync<ColesScrapeException>().WithMessage("*0 products*");
            db.PriceHistory.Should().BeEmpty();
            export.Verify(e => e.ExportAsync(It.IsAny<ScrapeExportRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ScrapeAsync_Throws_WhenFirstPageJsonIsMalformed()
        {
            var (scraper, _, _) = NewScraper(new FakePageSource("{\"pageProps\": [not json"));

            var act = () => scraper.ScrapeAsync(Bakery);

            await act.Should().ThrowAsync<ColesScrapeException>().WithMessage("*could not be parsed*");
        }

        [Fact]
        public async Task ScrapeAsync_Throws_WhenFirstPageIsBlocked()
        {
            var source = new FakePageSource(new ColesBlockedException("Coles returned an Imperva bot-block page"));
            var (scraper, db, _) = NewScraper(source);

            var act = () => scraper.ScrapeAsync(Bakery);

            await act.Should().ThrowAsync<ColesBlockedException>();
            db.PriceHistory.Should().BeEmpty();
        }

        [Fact]
        public async Task ScrapeAsync_SavesEarlierPagesThenThrows_WhenALaterPageFails()
        {
            var source = new FakePageSource(
                PageJson(4, 2, (1, "Bread", 3.5m), (2, "Rolls", 4m)),
                new ColesBlockedException("Coles returned an Imperva bot-block page"));
            var (scraper, db, export) = NewScraper(source);

            var act = () => scraper.ScrapeAsync(Bakery);

            await act.Should().ThrowAsync<ColesScrapeException>().WithMessage("*saved 2 products*");
            db.PriceHistory.Should().HaveCount(2);
            export.Verify(e => e.ExportAsync(It.IsAny<ScrapeExportRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>Returns the queued pages in order (a string is JSON, an exception is thrown).</summary>
        private class FakePageSource : IColesCategoryPageSource
        {
            private readonly Queue<object> _pages;

            public FakePageSource(params object[] pages) => _pages = new Queue<object>(pages);

            public List<int> RequestedPages { get; } = new();

            public Task<string> GetPageJsonAsync(ColesCategory category, int page, CancellationToken ct)
            {
                RequestedPages.Add(page);
                var next = _pages.Count > 0 ? _pages.Dequeue() : PageJson(0, 48);
                return next is Exception ex ? Task.FromException<string>(ex) : Task.FromResult((string)next);
            }
        }
    }

    public class ColesCategoriesTests
    {
        [Fact]
        public void Categories_HaveUniqueSlugsJobNamesAndOfferTypes()
        {
            var all = ColesCategories.All;
            all.Select(c => c.Slug).Should().OnlyHaveUniqueItems();
            all.Select(c => c.JobName).Should().OnlyHaveUniqueItems();
            all.Select(c => c.OfferType).Should().OnlyHaveUniqueItems();
            all.Select(c => c.Cron).Should().OnlyHaveUniqueItems();
        }

        [Fact]
        public void Categories_HaveValidQuartzCron()
        {
            ColesCategories.All.Should().OnlyContain(c => CronExpression.IsValidExpression(c.Cron));
        }

        [Fact]
        public void Categories_DoNotIncludeRetiredOrSkippedSlugs()
        {
            var slugs = ColesCategories.All.Select(c => c.Slug).ToList();
            slugs.Should().NotContain(new[] { "down-down", "dietary-world-foods", "back-to-school", "tobacco" });
            slugs.Should().HaveCount(20);
        }

        [Fact]
        public void FindBySlug_IsCaseInsensitive_AndReturnsNullForUnknown()
        {
            ColesCategories.FindBySlug("Health-Dietary")!.JobName.Should().Be("ColesHealthDietaryDomJob");
            ColesCategories.FindBySlug("down-down").Should().BeNull();
        }

        [Fact]
        public void ExportSourceAndEnvPrefix_FollowTheOldNaming()
        {
            var category = ColesCategories.FindBySlug("chips-chocolates-snacks")!;
            category.ExportSource.Should().Be("coles_chips_chocolates_snacks_json");
            category.EnvPrefix.Should().Be("COLES_CHIPS_CHOCOLATES_SNACKS");
        }
    }
}
