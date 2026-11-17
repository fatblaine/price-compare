using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PriceCompareCore.Config;
using PriceCompareWeb.Controllers.Models;
using PriceCompareWeb.Services;

namespace PriceCompareTests
{
    public class ScrapeImportSqlServiceTests
    {
        [Fact]
        public async Task GenerateAsync_WithSourceRows_ShouldNotEmitOnConflictAndShouldDedupeBySourceId()
        {
            var root = Path.Combine(Path.GetTempPath(), "pricecompare-tests", Guid.NewGuid().ToString("N"));
            var exportDirName = "export1";
            var exportDir = Path.Combine(root, exportDirName);
            Directory.CreateDirectory(exportDir);

            try
            {
                var productCsv = Path.Combine(exportDir, "product.csv");
                await File.WriteAllTextAsync(productCsv,
                    "shoptype,sourceid,name,brand,sizevalue,sizeunit,packageqty,categoryid,imageurl,lastseenat" + Environment.NewLine +
                    "1,abc123,First Name,,1,kg,1,,http://example.com/1.jpg,2026-02-03T10:00:00Z" + Environment.NewLine +
                    "1,abc123,Second Name,,1,kg,1,,http://example.com/2.jpg,2026-02-03T11:00:00Z");

                var service = new ScrapeImportSqlService(
                    Options.Create(new ScrapeExportOptions
                    {
                        ExportDir = root,
                        BatchSize = 100
                    }),
                    new TestHostEnvironment(root),
                    NullLogger<ScrapeImportSqlService>.Instance);

                var result = await service.GenerateAsync(
                    new ScrapeImportSqlRequest(exportDirName, OutputPath: null, IncludeSql: true, BatchSize: 100));

                var sql = await File.ReadAllTextAsync(result.OutputPath);

                Assert.DoesNotContain("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("SELECT DISTINCT ON (v.shoptype, v.sourceid)", sql, StringComparison.Ordinal);
                Assert.Contains("WHERE NOT EXISTS", sql, StringComparison.Ordinal);
                Assert.Contains("AND p.sourceid = d.sourceid", sql, StringComparison.Ordinal);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Fact]
        public async Task GenerateAsync_WhenSourceIdIsZero_ShouldTreatAsMissingSourceId()
        {
            var root = Path.Combine(Path.GetTempPath(), "pricecompare-tests", Guid.NewGuid().ToString("N"));
            var exportDirName = "export2";
            var exportDir = Path.Combine(root, exportDirName);
            Directory.CreateDirectory(exportDir);

            try
            {
                var productCsv = Path.Combine(exportDir, "product.csv");
                await File.WriteAllTextAsync(productCsv,
                    "shoptype,sourceid,name,brand,sizevalue,sizeunit,packageqty,categoryid,imageurl,lastseenat" + Environment.NewLine +
                    "1,0,Name A,,1,kg,1,,http://example.com/a.jpg,2026-02-03T10:00:00Z" + Environment.NewLine +
                    "1,0,Name B,,1,kg,1,,http://example.com/b.jpg,2026-02-03T11:00:00Z");

                var service = new ScrapeImportSqlService(
                    Options.Create(new ScrapeExportOptions
                    {
                        ExportDir = root,
                        BatchSize = 100
                    }),
                    new TestHostEnvironment(root),
                    NullLogger<ScrapeImportSqlService>.Instance);

                var result = await service.GenerateAsync(
                    new ScrapeImportSqlRequest(exportDirName, OutputPath: null, IncludeSql: true, BatchSize: 100));

                var sql = await File.ReadAllTextAsync(result.OutputPath);

                Assert.Contains("WHERE v.sourceid IS NULL", sql, StringComparison.Ordinal);
                Assert.DoesNotContain("WHERE v.sourceid IS NOT NULL", sql, StringComparison.Ordinal);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private const string FullProductHeader =
            "shoptype,sourceid,name,brand,sizevalue,sizeunit,packageqty,categoryid,imageurl,lastseenat," +
            "normalizedname,normalizedbrand,sizestandardvalue,sizestandardunit,sizeunknown,brandunknown,categoryunknown";

        // BTS-163: exports now carry a stockcode column; every product statement must write it,
        // and the updates must not wipe a stockcode that is already stored.
        [Fact]
        public async Task GenerateAsync_WithStockcodeColumn_WritesStockcodeInAllProductStatements()
        {
            var sql = await GenerateForCsvAsync(
                FullProductHeader + ",stockcode" + Environment.NewLine +
                "1,,Cocobella Coconut Water 1l,,1,l,1,,http://example.com/a.jpg,2026-11-17T10:00:00Z,cocobella coconut water 1l,,1,l,false,true,true,724514" + Environment.NewLine +
                "0,9736075,Morning Fresh Original 900mL,,900,ml,1,,http://example.com/b.jpg,2026-11-17T10:00:00Z,morning fresh original 900ml,,0.9,l,false,true,true,");

            Assert.Contains("'724514')", sql, StringComparison.Ordinal);
            Assert.Contains("stockcode = COALESCE(d.stockcode, p.stockcode)", sql, StringComparison.Ordinal);
            Assert.Contains("stockcode = COALESCE(v.stockcode, p.stockcode)", sql, StringComparison.Ordinal);
            Assert.Equal(2, CountOf(sql, "INSERT INTO product (shoptype, sourceid, name, brand, sizevalue, sizeunit, packageqty, categoryid, imageurl, lastseenat, normalizedname, normalizedbrand, sizestandardvalue, sizestandardunit, sizeunknown, brandunknown, categoryunknown, stockcode)"));
            // 4 VALUES aliases + 2 INSERT column lists, none left without the stockcode column
            Assert.Equal(6, CountOf(sql, "brandunknown, categoryunknown, stockcode)"));
            Assert.Equal(0, CountOf(sql, "brandunknown, categoryunknown)"));
        }

        [Fact]
        public async Task GenerateAsync_OldExportWithoutStockcodeColumn_StillImports()
        {
            var sql = await GenerateForCsvAsync(
                FullProductHeader + Environment.NewLine +
                "1,,Cocobella Coconut Water 1l,,1,l,1,,http://example.com/a.jpg,2026-11-10T10:00:00Z,cocobella coconut water 1l,,1,l,false,true,true");

            // The missing column becomes NULL, which COALESCE turns into "keep the stored stockcode".
            Assert.Contains("true, NULL)", sql, StringComparison.Ordinal);
            Assert.Contains("stockcode = COALESCE(v.stockcode, p.stockcode)", sql, StringComparison.Ordinal);
        }

        private static int CountOf(string text, string value)
        {
            var count = 0;
            for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            {
                count++;
            }
            return count;
        }

        private static async Task<string> GenerateForCsvAsync(string productCsvContent)
        {
            var root = Path.Combine(Path.GetTempPath(), "pricecompare-tests", Guid.NewGuid().ToString("N"));
            var exportDirName = "export-stockcode";
            var exportDir = Path.Combine(root, exportDirName);
            Directory.CreateDirectory(exportDir);

            try
            {
                await File.WriteAllTextAsync(Path.Combine(exportDir, "product.csv"), productCsvContent);

                var service = new ScrapeImportSqlService(
                    Options.Create(new ScrapeExportOptions { ExportDir = root, BatchSize = 100 }),
                    new TestHostEnvironment(root),
                    NullLogger<ScrapeImportSqlService>.Instance);

                var result = await service.GenerateAsync(
                    new ScrapeImportSqlRequest(exportDirName, OutputPath: null, IncludeSql: true, BatchSize: 100));

                return await File.ReadAllTextAsync(result.OutputPath);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private sealed class TestHostEnvironment : IHostEnvironment
        {
            public TestHostEnvironment(string contentRootPath)
            {
                ContentRootPath = contentRootPath;
                ContentRootFileProvider = new PhysicalFileProvider(contentRootPath);
            }

            public string EnvironmentName { get; set; } = "Development";
            public string ApplicationName { get; set; } = "PriceCompareTests";
            public string ContentRootPath { get; set; }
            public IFileProvider ContentRootFileProvider { get; set; }
        }
    }
}
