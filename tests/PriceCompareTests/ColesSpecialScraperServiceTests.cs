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
    public class ColesSpecialScraperServiceTests
    {
        private AppDbContext GetInMemoryDbContext()
        {
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(System.Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        [Fact(Skip = "ColesSpecialScraperService depends on Playwright + live Coles site; needs refactor to inject transport for unit testing.")]
        public async Task GetAllSpecialProductsAsync_Smoke()
        {
            var logger = new Mock<ILogger<ColesSpecialScraperService>>();
            var cache = new Mock<IDistributedCache>();
            var ingestion = new Mock<PriceCompareCore.Interfaces.IIngestionService>();
            using var dbContext = GetInMemoryDbContext();

            var service = new ColesSpecialScraperService(logger.Object, cache.Object, dbContext, ingestion.Object);

            await service.GetAllOnSpecialProductsAsync();
        }
    }
}
