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

        // BTS-163: the stockcode must not become the SourceId, or the prod SQL import (which matches on
        // (shoptype, sourceid)) would insert every Woolworths product again.
        [Fact]
        public async Task UpsertWwsAsync_StockcodeGoesToOwnColumnNotSourceId()
        {
            var dbContext = GetInMemoryDbContext();
            var service = new IngestionService(dbContext, new CategoryMappingService(dbContext));

            await service.UpsertWwsAsync(new[] { WwsDomProduct("Cocobella Coconut Water 1l", 724514) });

            var inserted = await dbContext.Products.SingleAsync();
            Assert.Null(inserted.SourceId);
            Assert.Equal("724514", inserted.Stockcode);
        }

        [Fact]
        public async Task UpsertWwsAsync_BackfillsStockcodeOnExistingProductAndKeepsIt()
        {
            var dbContext = GetInMemoryDbContext();
            var service = new IngestionService(dbContext, new CategoryMappingService(dbContext));

            // A product first seen before stockcodes were read.
            await service.UpsertWwsAsync(new[] { WwsDomProduct("Cocobella Coconut Water 1l", 0) });
            Assert.Null((await dbContext.Products.SingleAsync()).Stockcode);

            // Next scrape reads the stockcode: same row (matched by name), stockcode filled in.
            await service.UpsertWwsAsync(new[] { WwsDomProduct("Cocobella Coconut Water 1l", 724514) });
            var product = await dbContext.Products.SingleAsync();
            Assert.Equal("724514", product.Stockcode);
            Assert.Null(product.SourceId);

            // A later row without a stockcode does not wipe it.
            await service.UpsertWwsAsync(new[] { WwsDomProduct("Cocobella Coconut Water 1l", 0) });
            Assert.Equal("724514", (await dbContext.Products.SingleAsync()).Stockcode);
        }

        [Fact]
        public async Task UpsertWwsAsync_BarcodeStillUsedAsSourceId()
        {
            var dbContext = GetInMemoryDbContext();
            var service = new IngestionService(dbContext, new CategoryMappingService(dbContext));

            var item = WwsDomProduct("Cocobella Coconut Water 1l", 724514);
            item.Barcode = "9330071000123";
            await service.UpsertWwsAsync(new[] { item });

            var inserted = await dbContext.Products.SingleAsync();
            Assert.Equal("9330071000123", inserted.SourceId);
            Assert.Equal("724514", inserted.Stockcode);
        }

        private static WoolworthsSpecialProduct WwsDomProduct(string name, int stockcode) => new()
        {
            Stockcode = stockcode,
            Barcode = string.Empty,
            DisplayName = name,
            Brand = string.Empty,
            Price = 2.75m,
            IsOnSpecial = true,
            CupString = string.Empty,
            LargeImageFile = "http://example.com/x.jpg",
            ScrapedAt = DateTime.UtcNow
        };

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

        [Fact]
        public void MapWoolworthsProducts_WhenBarcodeAndStockcodeMissing_ShouldKeepSourceIdNull()
        {
            // Arrange
            var dbContext = GetInMemoryDbContext();
            var mapper = new CategoryMappingService(dbContext);
            var service = new IngestionService(dbContext, mapper);

            var product = new WoolworthsSpecialProduct
            {
                Stockcode = 0,
                Barcode = string.Empty,
                DisplayName = "WWS DOM Product",
                Price = 2.99m,
                LargeImageFile = "http://example.com/wws-dom.jpg",
                ScrapedAt = DateTime.UtcNow
            };

            // Act
            var mapped = service.MapWoolworthsProducts(new[] { product }).Single();

            // Assert
            Assert.Null(mapped.SourceId);
        }
    }
}
