using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using PriceCompareCore.Interfaces;
using PriceCompareCore.Services;
using PriceCompareData.Data;

namespace PriceCompareTests
{
    public class ColesDownScraperServiceTests
    {
        private AppDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        [Fact]
        public async Task GetAllDownDownProductsAsync_ShouldInsertPriceHistory_WhenHtmlValid()
        {
            var html = @"<html><body>
                <div id='coles-targeting-product-tiles'>
                    <section data-testid='product-tile'>
                    <a class='product__link'>
                        <h2 class='product__title'>Test Product</h2>
                    </a>
                    <span class='price'>$19.50</span>
                    <div class='price__calculation_method'>$0.39 per 1ea</div>
                    <div class='price__was'><strong>Was $34.00</strong></div>
                    <img data-testid='product-image' src='http://example.com/test.jpg' />
                    </section>
                </div>
                </body></html>";

            var handler = new StubHttpMessageHandler(html);
            var httpClient = new HttpClient(handler);

            var logger = new Mock<ILogger<ColesDownScraperService>>();
            var cache = new Mock<IDistributedCache>();
            var ingestion = new Mock<IIngestionService>();
            var dbContext = GetInMemoryDbContext();

            var service = new ColesDownScraperService(
                httpClient, logger.Object, cache.Object, dbContext, ingestion.Object);

            // Act
            var result = await service.GetAllDownDownProductsAsync();

            // Assert
            Assert.Single(result);
            Assert.Equal("Test Product", result[0].Name);
            Assert.Equal(19.50m, result[0].CurrentPrice);
            Assert.Equal(34.00m, result[0].OriginalPrice);
            Assert.Contains(dbContext.PriceHistory, ph => ph.Name == "Test Product");
        }

        private class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly string _response;
            private bool _servedOnce = false;

            public StubHttpMessageHandler(string response) => _response = response;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (_servedOnce)
                {
                    // Serve a different response the second time to simulate the initial HTML load
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("<html><body><div id='coles-targeting-product-tiles'></div></body></html>")
                    });
                }

                _servedOnce = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_response)
                });
            }
        }
    }
}