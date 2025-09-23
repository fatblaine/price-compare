using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PriceCompareCore.Services;
using PriceCompareData.Data;
using PriceCompareData.DTOs;
using PriceCompareData.Entities;
using PriceCompareData.Entities.History;

namespace PriceCompareTests;

public class UnitTest1
{
    [Fact]
    public async Task GetPriceHistoryAsync_ShouldFilterByNameOfferShopAsync()
    {
        var dbName = Guid.NewGuid().ToString();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        await using var arrangeContext = new AppDbContext(options);
        arrangeContext.PriceHistory.AddRange(
            new PriceHistory
            {
                Name = "Apple",
                OfferType = 1,
                ShopType = 1,
                CurrentPrice = 2.50m,
                ScrapedAt = DateTime.UtcNow.AddDays(-1),
                ImageUrl = "https://dummy.image/apple"
            },
                new PriceHistory
                {
                    Name = "Apple",
                    OfferType = 1,
                    ShopType = 1,
                    CurrentPrice = 2.30m,
                    ScrapedAt = DateTime.UtcNow.AddDays(-2),
                    ImageUrl = "https://dummy.image/apple"
                },
                new PriceHistory
                {
                    Name = "Orange",
                    OfferType = 1,
                    ShopType = 1,
                    CurrentPrice = 1.50m,
                    ScrapedAt = DateTime.UtcNow.AddDays(-1),
                    ImageUrl = "https://dummy.image/orange"
                },
                new PriceHistory
                {
                    Name = "Apple",
                    OfferType = 0,
                    ShopType = 1,
                    CurrentPrice = 2.20m,
                    ScrapedAt = DateTime.UtcNow.AddDays(-1),
                    ImageUrl = "https://dummy.image/apple"
                }
        );
        await arrangeContext.SaveChangesAsync();

        await using var actContext = new AppDbContext(options);
        var service = new ColesDownScraperService(
            httpClient: new HttpClient(),
            logger: NullLogger<ColesDownScraperService>.Instance,
            cache: null,
            ingestion: null,
            dbContext: actContext);

        var result = await service.GetPriceHistoryAsync("Apple", 1, 1);

        Assert.Equal(2, result.Count);
        Assert.All(result, r =>
        {
            Assert.Equal("Apple", r.Name);
            Assert.Equal(1, r.OfferType);
            Assert.Equal(1, r.ShopType);
        });
    }
}