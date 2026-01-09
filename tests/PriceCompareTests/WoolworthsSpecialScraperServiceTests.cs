using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using PriceCompareCore.Services;
using PriceCompareData.Data;
using Xunit;

namespace PriceCompareTests
{
    public class WoolworthsSpecialScraperServiceTests
    {
        private AppDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        [Fact]
        public void ParseProducts_ShouldReturnProducts()
        {
            // Arrange
            var fakeJson = @"[
              { ""Stockcode"": 456, ""Name"": ""WWS Bread"", ""Price"": 3.5, ""WasPrice"": 4.0,
                ""LargeImageFile"": ""http://example.com/bread.jpg"" }
            ]";

            var logger = new Mock<ILogger<WoolworthsSpecialScraperService>>();
            var cache = new Mock<IDistributedCache>();
            var ingestion = new Mock<PriceCompareCore.Interfaces.IIngestionService>();
            var dbContext = GetInMemoryDbContext();

            var service = new WoolworthsSpecialScraperService(
                logger.Object, cache.Object, dbContext, ingestion.Object);

            // Act
            var result = service.ParseProducts(fakeJson);

            // Assert
            Assert.Single(result);
            Assert.Equal("WWS Bread", result[0].DisplayName);
            Assert.Equal(3.5m, result[0].Price);
            Assert.Equal(4.0m, result[0].WasPrice);
            Assert.Equal("http://example.com/bread.jpg", result[0].LargeImageFile);
        }
    }
}
