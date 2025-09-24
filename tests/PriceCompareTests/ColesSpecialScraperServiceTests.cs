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
    public class ColesSpecialScraperServiceTests
    {
        private AppDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        [Fact]
        public async Task GetAllSpecialProductsAsync_ShouldParseProducts_WhenApiReturnsValidJson()
        {
            // Arrange:
            var fakeJson = @"{
                ""pageProps"": {
                    ""searchResults"": {
                    ""results"": [
                        {
                        ""_type"": ""PRODUCT"",
                        ""id"": 123011,
                        ""name"": ""Coles Milk"",
                        ""pricing"": { ""now"": 2.5, ""was"": 3.0 },
                        ""imageUris"": [ { ""uri"": ""http://example.com/milk.jpg"" } ]
                        }
                    ]
                    }
                }
            }";

            var handler = new StubHttpMessageHandler(fakeJson);
            var httpClient = new HttpClient(handler);

            var logger = new Mock<ILogger<ColesSpecialScraperService>>();
            var cache = new Mock<IDistributedCache>();
            var ingestion = new Mock<IIngestionService>();
            var dbContext = GetInMemoryDbContext();

            var service = new ColesSpecialScraperService(
                httpClient, logger.Object, cache.Object, dbContext, ingestion.Object);

            // Act
            var result = await service.GetAllOnSpecialProductsAsync();

            // Assert
            Assert.NotEmpty(result);
            Assert.Equal("Coles Milk", result[0].Name);
            Assert.Equal(2.5m, result[0].CurrentPrice);
            Assert.Equal(3.0m, result[0].OriginalPrice);


        }

        // A stubbed HttpMessageHandler that fakes Coles "on special" responses
        private class StubHttpMessageHandler : HttpMessageHandler
        {
            private int _callCount = 0;
            private readonly string _fakeJson;

            public StubHttpMessageHandler(string fakeJson)
            {
                _fakeJson = fakeJson;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                _callCount++;

                // 1st call → return the HTML page containing the buildId
                if (_callCount == 1)
                {
                    var html = @"<html><body>
                        <script id='__NEXT_DATA__' type='application/json'>
                        { ""buildId"": ""fake-build-id"" }
                        </script>
                    </body></html>";

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(html)
                    });
                }
                // 2nd call → return a JSON response with one product
                else if (_callCount == 2)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(_fakeJson)
                    });
                }
                // 3rd call (and any further) → return an empty results page to stop pagination
                else
                {
                    var emptyJson = @"{ ""pageProps"": { ""searchResults"": { ""results"": [] } } }";

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(emptyJson)
                    });
                }
            }
        }
    }
}