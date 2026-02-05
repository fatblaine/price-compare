using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceCompareCore.Config;
using PriceCompareCore.Interfaces;
using PriceCompareData.Entities.Compare;
using PriceCompareData.Entities.History;

namespace PriceCompareCore.Services
{
    public class ScrapeExportService : IScrapeExportService
    {
        private readonly ScrapeExportOptions _options;
        private readonly ILogger<ScrapeExportService> _logger;

        public ScrapeExportService(
            IOptions<ScrapeExportOptions> options,
            ILogger<ScrapeExportService> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task<ScrapeExportResult?> ExportAsync(ScrapeExportRequest request, CancellationToken ct = default)
        {
            if (!_options.Enabled)
            {
                return null;
            }

            var root = string.IsNullOrWhiteSpace(_options.ExportDir) ? "exports" : _options.ExportDir;
            var rootPath = Path.IsPathRooted(root)
                ? root
                : Path.Combine(Directory.GetCurrentDirectory(), root);

            var source = SanitizePathSegment(request.Source);
            var folderName = $"{request.ExportedAtUtc:yyyyMMdd_HHmmss_fff}_{source}";
            var exportDir = Path.Combine(rootPath, folderName);

            Directory.CreateDirectory(exportDir);

            string? priceHistoryPath = null;
            if (request.PriceHistory.Count > 0)
            {
                priceHistoryPath = Path.Combine(exportDir, "pricehistory.csv");
                await WritePriceHistoryCsvAsync(priceHistoryPath, request.PriceHistory, ct);
            }

            string? productPath = null;
            if (request.Products.Count > 0)
            {
                productPath = Path.Combine(exportDir, "product.csv");
                await WriteProductCsvAsync(productPath, request.Products, ct);
            }

            var manifestPath = Path.Combine(exportDir, "manifest.json");
            var manifest = new
            {
                request.Source,
                request.ExportedAtUtc,
                PriceHistoryRows = request.PriceHistory.Count,
                ProductRows = request.Products.Count,
                PriceHistoryPath = priceHistoryPath != null ? Path.GetFileName(priceHistoryPath) : null,
                ProductPath = productPath != null ? Path.GetFileName(productPath) : null
            };

            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                ct);

            _logger.LogInformation("Scrape export created at {ExportDir}", exportDir);

            return new ScrapeExportResult(
                exportDir,
                request.PriceHistory.Count,
                request.Products.Count,
                priceHistoryPath,
                productPath,
                manifestPath);
        }

        private static async Task WritePriceHistoryCsvAsync(string path, IReadOnlyList<PriceHistory> rows, CancellationToken ct)
        {
            var headers = new[]
            {
                "name",
                "currentprice",
                "imageurl",
                "scrapedat",
                "offertype",
                "shoptype"
            };

            await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));

            await CsvWriter.WriteHeaderAsync(writer, headers, ct);

            foreach (var row in rows)
            {
                var fields = new[]
                {
                    row.Name ?? string.Empty,
                    row.CurrentPrice.ToString(CultureInfo.InvariantCulture),
                    row.ImageUrl ?? string.Empty,
                    row.ScrapedAt.ToString("O", CultureInfo.InvariantCulture),
                    row.OfferType?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    row.ShopType?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
                };

                await CsvWriter.WriteRowAsync(writer, fields, ct);
            }
        }

        private static async Task WriteProductCsvAsync(string path, IReadOnlyList<Product> rows, CancellationToken ct)
        {
            var headers = new[]
            {
                "shoptype",
                "sourceid",
                "name",
                "brand",
                "sizevalue",
                "sizeunit",
                "packageqty",
                "categoryid",
                "imageurl",
                "lastseenat"
            };

            await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));

            await CsvWriter.WriteHeaderAsync(writer, headers, ct);

            foreach (var row in rows)
            {
                var fields = new[]
                {
                    row.ShopType?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    row.SourceId ?? string.Empty,
                    row.Name ?? string.Empty,
                    row.Brand ?? string.Empty,
                    row.SizeValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    row.SizeUnit ?? string.Empty,
                    row.PackageQty?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    row.CategoryId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    row.ImageUrl ?? string.Empty,
                    row.LastSeenAt.ToString("O", CultureInfo.InvariantCulture)
                };

                await CsvWriter.WriteRowAsync(writer, fields, ct);
            }
        }

        private static string SanitizePathSegment(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(value
                .Select(ch => invalid.Contains(ch) ? '_' : ch)
                .ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "export" : cleaned;
        }

        private static class CsvWriter
        {
            public static async Task WriteHeaderAsync(StreamWriter writer, IReadOnlyList<string> headers, CancellationToken ct)
            {
                await WriteRowAsync(writer, headers, ct);
            }

            public static async Task WriteRowAsync(StreamWriter writer, IReadOnlyList<string> fields, CancellationToken ct)
            {
                var line = string.Join(",", fields.Select(Escape));
                await writer.WriteLineAsync(line.AsMemory(), ct);
            }

            private static string Escape(string value)
            {
                if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
                {
                    return $"\"{value.Replace("\"", "\"\"")}\"";
                }

                return value;
            }
        }
    }
}
