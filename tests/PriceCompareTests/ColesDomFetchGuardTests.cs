using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PriceCompareCore.Exceptions;
using PriceCompareCore.Interfaces;
using PriceCompareCore.Services;
using PriceCompareData.Data;

namespace PriceCompareTests
{
    // BTS-156: Coles category scrapes must fail loudly instead of "succeeding" with 0 products.
    public class ColesDomFetchGuardTests
    {
        private const string Url = "https://www.coles.com.au/_next/data/test/en/browse/bakery.json?slug=bakery";
        private const string ImpervaPage =
            "<!DOCTYPE html><html><head><title>Pardon Our Interruption</title></head>" +
            "<body><script src=\"/_Incapsula_Resource?x=1\"></script></body></html>";

        private static string ProductJson(params (int Id, string Name, decimal Price)[] products)
        {
            var results = string.Join(",", products.Select(p =>
                $"{{\"_type\":\"PRODUCT\",\"id\":{p.Id},\"name\":\"{p.Name}\",\"brand\":\"Coles\",\"size\":\"1 each\",\"pricing\":{{\"now\":{p.Price}}}}}"));
            return $"{{\"pageProps\":{{\"searchResults\":{{\"results\":[{results}]}}}}}}";
        }

        private static ColesBlockBreaker NewBreaker() => new(TimeSpan.FromMinutes(180));

        private static Task<string> Fetch(SequenceHandler handler, ColesBlockBreaker breaker) =>
            ColesDomFetchGuard.FetchJsonAsync(new HttpClient(handler), Url, NullLogger.Instance, CancellationToken.None, breaker);

        [Fact]
        public async Task FetchJsonAsync_ReturnsBody_WhenResponseIsJson()
        {
            var json = ProductJson((1, "Bread", 3.5m));
            var body = await Fetch(SequenceHandler.Json(json), NewBreaker());
            body.Should().Be(json);
        }

        [Fact]
        public async Task FetchJsonAsync_ThrowsBlockedAndTripsBreaker_WhenImpervaPageReturnedWith200()
        {
            var breaker = NewBreaker();
            var handler = new SequenceHandler((HttpStatusCode.OK, ImpervaPage, "text/html"));

            var act = () => Fetch(handler, breaker);

            (await act.Should().ThrowAsync<ColesBlockedException>()).Which.Message.Should().Contain("Imperva");
            breaker.BlockedUntilUtc.Should().NotBeNull();
        }

        [Fact]
        public async Task FetchJsonAsync_SkipsRequest_WhileBreakerIsOpen()
        {
            var breaker = NewBreaker();
            breaker.Trip();
            var handler = SequenceHandler.Json(ProductJson((1, "Bread", 3.5m)));

            var act = () => Fetch(handler, breaker);

            (await act.Should().ThrowAsync<ColesBlockedException>()).Which.Message.Should().Contain("cooling down");
            handler.Calls.Should().Be(0);
        }

        [Fact]
        public async Task Breaker_Closes_AfterCooldown()
        {
            var now = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
            var breaker = new ColesBlockBreaker(TimeSpan.FromMinutes(180), () => now);
            breaker.Trip();

            now = now.AddMinutes(181);
            var body = await Fetch(SequenceHandler.Json("{}"), breaker);

            body.Should().Be("{}");
            breaker.BlockedUntilUtc.Should().BeNull();
        }

        [Fact]
        public void Breaker_StaysClosed_WhenCooldownIsZero()
        {
            var breaker = new ColesBlockBreaker(TimeSpan.Zero);
            breaker.Trip();

            var act = () => breaker.ThrowIfOpen(Url);

            act.Should().NotThrow();
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.GatewayTimeout)]
        [InlineData(HttpStatusCode.NotFound)]
        public async Task FetchJsonAsync_Throws_WhenStatusIsNotSuccess(HttpStatusCode status)
        {
            var breaker = NewBreaker();
            var handler = new SequenceHandler((status, "<html>error</html>", "text/html"));

            var act = () => Fetch(handler, breaker);

            (await act.Should().ThrowAsync<ColesScrapeException>()).Which.Message.Should().Contain($"HTTP {(int)status}");
            breaker.BlockedUntilUtc.Should().BeNull("only the Imperva page opens the breaker");
        }

        [Fact]
        public async Task FetchJsonAsync_Throws_WhenBodyIsNotJson()
        {
            var handler = new SequenceHandler((HttpStatusCode.OK, "<html>maintenance</html>", "text/html"));

            var act = () => Fetch(handler, NewBreaker());

            (await act.Should().ThrowAsync<ColesScrapeException>()).Which.Message.Should().Contain("non-JSON");
        }

        [Fact]
        public void EnsureProducts_Throws_WhenNothingWasExtracted()
        {
            var act = () => ColesDomFetchGuard.EnsureProducts(0, Url);
            act.Should().Throw<ColesScrapeException>().WithMessage("*0 products*");
        }

        // ---- scraper-level behaviour (Bakery stands in for all Coles category scrapers) ----

        private static (ColesBakeryDomScraperService Scraper, AppDbContext Db, Mock<IScrapeExportService> Export) NewBakeryScraper(SequenceHandler handler)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
            var export = new Mock<IScrapeExportService>();
            var scraper = new ColesBakeryDomScraperService(
                new HttpClient(handler),
                new Mock<ILogger<ColesBakeryDomScraperService>>().Object,
                new Mock<IDistributedCache>().Object,
                db,
                new Mock<IIngestionService>().Object,
                export.Object);
            return (scraper, db, export);
        }

        [Fact]
        public async Task ScrapeAsync_ReturnsProducts_WhenAllPagesAreValid()
        {
            var handler = new SequenceHandler(
                (HttpStatusCode.OK, ProductJson((1, "Bread", 3.5m), (2, "Rolls", 4m)), "application/json"),
                (HttpStatusCode.OK, ProductJson(), "application/json"));
            var (scraper, db, _) = NewBakeryScraper(handler);

            var products = await scraper.ScrapeAsync();

            products.Should().HaveCount(2);
            db.PriceHistory.Count().Should().Be(2);
        }

        [Fact]
        public async Task ScrapeAsync_Throws_WhenFirstPageHasNoProducts()
        {
            var (scraper, db, export) = NewBakeryScraper(SequenceHandler.Json(ProductJson()));

            var act = () => scraper.ScrapeAsync();

            await act.Should().ThrowAsync<ColesScrapeException>().WithMessage("*0 products*");
            db.PriceHistory.Count().Should().Be(0);
            export.Verify(e => e.ExportAsync(It.IsAny<ScrapeExportRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ScrapeAsync_Throws_WhenFirstPageJsonIsMalformed()
        {
            var (scraper, _, _) = NewBakeryScraper(SequenceHandler.Json("{\"pageProps\": [not json"));

            var act = () => scraper.ScrapeAsync();

            await act.Should().ThrowAsync<ColesScrapeException>().WithMessage("*could not be parsed*");
        }

        [Fact]
        public async Task ScrapeAsync_SavesEarlierPagesThenThrows_WhenALaterPageFails()
        {
            var handler = new SequenceHandler(
                (HttpStatusCode.OK, ProductJson((1, "Bread", 3.5m), (2, "Rolls", 4m)), "application/json"),
                (HttpStatusCode.OK, "<html>maintenance</html>", "text/html"));
            var (scraper, db, export) = NewBakeryScraper(handler);

            var act = () => scraper.ScrapeAsync();

            await act.Should().ThrowAsync<ColesScrapeException>().WithMessage("*saved 2 products*");
            db.PriceHistory.Count().Should().Be(2);
            export.Verify(e => e.ExportAsync(It.IsAny<ScrapeExportRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>Returns the queued responses in order; repeats the last one when the queue runs out.</summary>
        private class SequenceHandler : HttpMessageHandler
        {
            private readonly Queue<(HttpStatusCode Status, string Body, string ContentType)> _responses;
            private (HttpStatusCode Status, string Body, string ContentType) _last;

            public SequenceHandler(params (HttpStatusCode Status, string Body, string ContentType)[] responses)
            {
                _responses = new Queue<(HttpStatusCode, string, string)>(responses);
                _last = responses[^1];
            }

            public static SequenceHandler Json(string body) => new((HttpStatusCode.OK, body, "application/json"));

            public int Calls { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                var next = _responses.Count > 0 ? _responses.Dequeue() : _last;
                _last = next;
                return Task.FromResult(new HttpResponseMessage(next.Status)
                {
                    Content = new StringContent(next.Body, Encoding.UTF8, next.ContentType)
                });
            }
        }
    }
}
