using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PriceCompareCore.Services;
using PriceCompareData.Data;
using PriceCompareData.Entities;
using PriceCompareData.Entities.Scraping;

namespace PriceCompareTests
{
    public class IngestionServiceTests
    {
        private AppDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        [Fact]
        public async Task UpsertColesSpecialAsync_ShouldInsertAndUpdateProduct()
        {
            // Arrange
            var dbContext = GetInMemoryDbContext();
            var mapper = new CategoryMappingService(dbContext);
            var service = new IngestionService(dbContext, mapper);

            var product = new ColesSpecialProduct
            {
                Id = 1,
                Name = "Test Cola",
                CurrentPrice = 5,
                OriginalPrice = 6,
                ImageUrl = "http://example.com/cola.jpg",
                ScrapedAt = DateTime.UtcNow
            };

            // Act
            await service.UpsertColesSpecialAsync(new[] { product });
            var inserted = await dbContext.Products.FirstOrDefaultAsync();

            // Assert
            Assert.NotNull(inserted);
            Assert.Equal("Test Cola", inserted.Name);

            // Act - update
            product.Name = "Updated Cola";
            await service.UpsertColesSpecialAsync(new[] { product });
            var updated = await dbContext.Products.FirstOrDefaultAsync();

            // Assert
            Assert.Equal("Updated Cola", updated.Name);
        }

        [Fact]
        public async Task UpsertColesDownAsync_ShouldInsertAndUpdateProduct()
        {
            // Arrange
            var dbContext = GetInMemoryDbContext();
            var mapper = new CategoryMappingService(dbContext);
            var service = new IngestionService(dbContext, mapper);

            var product = new ColesDownProduct
            {
                Id = 1,
                Name = "Test Bread",
                CurrentPrice = 3,
                OriginalPrice = 4,
                ImageUrl = "http://example.com/bread.jpg",
                ScrapedAt = DateTime.UtcNow
            };

            // Act
            await service.UpsertColesDownAsync(new[] { product });
            var inserted = await dbContext.Products.FirstOrDefaultAsync();

            // Assert
            Assert.NotNull(inserted);
            Assert.Equal("Test Bread", inserted.Name);

            // Act - update
            product.Name = "Updated Bread";
            await service.UpsertColesDownAsync(new[] { product });
            var updated = await dbContext.Products.FirstOrDefaultAsync();

            // Assert
            Assert.Equal("Updated Bread", updated.Name);
        }

        [Fact]
        public async Task UpsertWwsAsync_ShouldInsertAndUpdateProduct()
        {
            // Arrange
            var dbContext = GetInMemoryDbContext();
            var mapper = new CategoryMappingService(dbContext);
            var service = new IngestionService(dbContext, mapper);

            var product = new WoolworthsSpecialProduct
            {
                Stockcode = 1,
                DisplayName = "Test WWS",
                Price = 4,
                WasPrice = 5,
                LargeImageFile = "http://example.com/wws.jpg",
                ScrapedAt = DateTime.UtcNow
            };

            // Act
            await service.UpsertWwsAsync(new[] { product });
            var inserted = await dbContext.Products.FirstOrDefaultAsync();

            // Assert
            Assert.NotNull(inserted);
            Assert.Equal("Test WWS", inserted.Name);

            // Act - update
            product.DisplayName = "Updated WWS";
            await service.UpsertWwsAsync(new[] { product });
            var updated = await dbContext.Products.FirstOrDefaultAsync();

            // Assert
            Assert.Equal("Updated WWS", updated.Name);
        }
    }
}