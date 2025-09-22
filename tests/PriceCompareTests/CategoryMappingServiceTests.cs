using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PriceCompareCore.Services;
using PriceCompareData.Data;

namespace PriceCompareTests
{
    public class CategoryMappingServiceTests
    {
        private AppDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        public static IEnumerable<object[]> ParseSpecTestData => new List<object[]>
        {
            new object[] { "Coca Cola 500ml 6 pack", (decimal?)500, "ml", 6 },
            new object[] { "Some Unknown Product", null, null, null },
            new object[] { "", null, null, null },
        };

        [Theory]
        [MemberData(nameof(ParseSpecTestData))]
        public void ParseSpec_ValidInputs_ReturnsExpectedValues(
            string input, decimal? expectedSize, string? expectedUnit, int? expectedPkg)
        {
            var dbContext = GetInMemoryDbContext();
            var service = new CategoryMappingService(dbContext);

            var (sizeValue, sizeUnit, pkgQty) = service.ParseSpec(input);

            Assert.Equal(expectedSize, sizeValue);
            Assert.Equal(expectedUnit, sizeUnit);
            Assert.Equal(expectedPkg, pkgQty);
        }
    }
}