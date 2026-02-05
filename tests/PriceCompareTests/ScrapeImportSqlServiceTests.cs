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
