using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceCompareCore.Utils;
using PriceCompareData.Data;

namespace PriceCompareCore.Services.Catalogue
{
    public interface ICatalogueFetchService
    {
        /// <summary>
        /// Fetches the retailer's next-week catalogue, stores it and matches it to products.
        /// <paramref name="failIfNotPublished"/> = false returns a skipped result when the next
        /// catalogue is not out yet (early Monday runs); true throws (Tuesday's last chance).
        /// </summary>
        Task<CatalogueFetchResult> RunAsync(int shopType, bool failIfNotPublished, CancellationToken ct = default);
    }

    /// <summary>Runs a generated SQL script as one command.</summary>
    public interface ICatalogueSqlExecutor
    {
        Task ExecuteAsync(string sql, CancellationToken ct);
    }

    /// <summary>Executes on AppDbContext's own connection (no EF parameter parsing, so braces in names are safe).</summary>
    public sealed class DbContextCatalogueSqlExecutor : ICatalogueSqlExecutor
    {
        private readonly AppDbContext _db;

        public DbContextCatalogueSqlExecutor(AppDbContext db)
        {
            _db = db;
        }

        public async Task ExecuteAsync(string sql, CancellationToken ct)
        {
            var connection = _db.Database.GetDbConnection();
            var opened = false;
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
                opened = true;
            }

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.CommandTimeout = 300;
                await command.ExecuteNonQueryAsync(ct);
            }
            finally
            {
                if (opened)
                {
                    await connection.CloseAsync();
                }
            }
        }
    }

    public sealed class CatalogueFetchService : ICatalogueFetchService
    {
        private readonly ISaleFinderClient _client;
        private readonly ICatalogueSqlExecutor _executor;
        private readonly AppDbContext _db;
        private readonly CatalogueOptions _options;
        private readonly ILogger<CatalogueFetchService> _logger;
        private readonly Func<DateOnly> _today;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        public CatalogueFetchService(
            ISaleFinderClient client,
            ICatalogueSqlExecutor executor,
            AppDbContext db,
            IOptions<CatalogueOptions> options,
            ILogger<CatalogueFetchService> logger)
            : this(client, executor, db, options, logger, SydneyClock.Today, Task.Delay)
        {
        }

        /// <summary>For tests: fixed clock and no real delays.</summary>
        public CatalogueFetchService(
            ISaleFinderClient client,
            ICatalogueSqlExecutor executor,
            AppDbContext db,
            IOptions<CatalogueOptions> options,
            ILogger<CatalogueFetchService> logger,
            Func<DateOnly> today,
            Func<TimeSpan, CancellationToken, Task> delay)
        {
            _client = client;
            _executor = executor;
            _db = db;
            _options = options.Value;
            _logger = logger;
            _today = today;
            _delay = delay;
        }

        public async Task<CatalogueFetchResult> RunAsync(int shopType, bool failIfNotPublished, CancellationToken ct = default)
        {
            if (!_options.Enabled)
            {
                return new CatalogueFetchResult { ShopType = shopType, Skipped = true, Message = "Catalogue:Enabled is false." };
            }

            if (!_options.Retailers.TryGetValue(shopType, out var retailer) || retailer.RetailerId <= 0 || string.IsNullOrWhiteSpace(retailer.StoreId))
            {
                throw new InvalidOperationException($"Catalogue:Retailers:{shopType} is not configured.");
            }

            var today = _today();
            var sales = await _client.GetSalesAsync(shopType, retailer, ct);
            var next = SaleFinderParser.PickNextSale(sales, today, retailer.SaleNameContains);
            if (next is null)
            {
                var message = $"No catalogue starting after {today:yyyy-MM-dd} named like '{retailer.SaleNameContains}' "
                              + $"(retailer {retailer.RetailerId}, store {retailer.StoreId}; {sales.Count} sales listed).";
                if (failIfNotPublished)
                {
                    throw new InvalidOperationException(message);
                }

                _logger.LogInformation("Catalogue shop {ShopType}: {Message}", shopType, message);
                return new CatalogueFetchResult { ShopType = shopType, Skipped = true, Message = message };
            }

            var list = await _client.GetItemListAsync(next.SaleId, retailer, ct);
            var items = SaleFinderParser.ParseItems(list.ContentHtml, out var listSkus);
            if (items.Count == 0)
            {
                throw new InvalidOperationException($"Catalogue sale {next.SaleId} ({next.SaleName}) parsed 0 items; the SaleFinder format has probably changed.");
            }

            var withSkus = new List<CatalogueItemWithSkus>(items.Count);
            var detailFailures = 0;
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                IReadOnlyList<string> skus = Array.Empty<string>();
                var ok = false;
                for (var attempt = 1; attempt <= 3 && !ok; attempt++)
                {
                    try
                    {
                        skus = await _client.GetItemSkusAsync(item.ItemId, retailer, ct);
                        ok = true;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Catalogue item {ItemId} detail attempt {Attempt} failed", item.ItemId, attempt);
                        await _delay(TimeSpan.FromMilliseconds(Math.Max(_options.ItemDelayMs, 500) * attempt), ct);
                    }
                }

                if (!ok)
                {
                    detailFailures++;
                }

                if (skus.Count == 0 && listSkus.TryGetValue(item.ItemId, out var listSku))
                {
                    skus = new[] { listSku };
                }

                withSkus.Add(new CatalogueItemWithSkus(item, skus));
                if (_options.ItemDelayMs > 0)
                {
                    await _delay(TimeSpan.FromMilliseconds(_options.ItemDelayMs), ct);
                }
            }

            var itemsWithSku = withSkus.Count(i => i.Skus.Count > 0);
            if (detailFailures > items.Count * _options.MaxDetailFailureRatio)
            {
                throw new InvalidOperationException(
                    $"Catalogue sale {next.SaleId}: {detailFailures}/{items.Count} item-detail requests failed.");
            }

            if (items.Count - itemsWithSku > items.Count * _options.MaxItemsWithoutSkuRatio)
            {
                throw new InvalidOperationException(
                    $"Catalogue sale {next.SaleId}: {items.Count - itemsWithSku}/{items.Count} items have no product codes; the item-detail format has probably changed.");
            }

            var region = string.IsNullOrWhiteSpace(list.AreaName) ? retailer.StoreId : list.AreaName;
            var sql = CatalogueSqlWriter.Build(shopType, next.SaleId, region, list.StartDate, list.EndDate, DateTime.UtcNow, withSkus);

            string? exportPath = null;
            if (string.Equals(_options.Mode, "Export", StringComparison.OrdinalIgnoreCase))
            {
                exportPath = await WriteExportAsync(shopType, next.SaleId, sql, ct);
            }

            await _executor.ExecuteAsync(sql, ct);

            var skuCount = withSkus.Sum(i => i.Skus.Count);
            var (skusMatched, itemsMatched) = await CountMatchesAsync(shopType, next.SaleId, ct);
            var result = new CatalogueFetchResult
            {
                ShopType = shopType,
                SaleId = next.SaleId,
                SaleName = next.SaleName,
                Region = region,
                ValidFrom = list.StartDate,
                ValidTo = list.EndDate,
                Items = items.Count,
                ItemsWithSku = itemsWithSku,
                Skus = skuCount,
                SkusMatched = skusMatched,
                ItemsMatched = itemsMatched,
                DetailFailures = detailFailures,
                ExportPath = exportPath,
            };

            _logger.LogInformation(
                "Catalogue shop {ShopType} sale {SaleId} ({Region}, {ValidFrom}..{ValidTo}): items={Items} withSku={WithSku} skus={Skus} skusMatched={SkusMatched} itemsMatched={ItemsMatched} detailFailures={Failures}",
                shopType, next.SaleId, region, list.StartDate, list.EndDate, result.Items, itemsWithSku, skuCount, skusMatched, itemsMatched, detailFailures);
            return result;
        }

        private async Task<(int SkusMatched, int ItemsMatched)> CountMatchesAsync(int shopType, int saleId, CancellationToken ct)
        {
            var matched = _db.CatalogueSpecialSkus.AsNoTracking()
                .Where(k => k.ShopType == shopType && k.SaleId == saleId && k.ProductId != null);
            var skus = await matched.CountAsync(ct);
            var items = await matched.Select(k => k.ItemId).Distinct().CountAsync(ct);
            return (skus, items);
        }

        private async Task<string> WriteExportAsync(int shopType, int saleId, string sql, CancellationToken ct)
        {
            var root = string.IsNullOrWhiteSpace(_options.ExportDir) ? "exports" : _options.ExportDir;
            var rootPath = Path.IsPathRooted(root) ? root : Path.Combine(Directory.GetCurrentDirectory(), root);
            var shop = shopType == 1 ? "woolworths" : "coles";
            var dir = Path.Combine(rootPath, $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_catalogue_{shop}_{saleId}");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "import.sql");
            await File.WriteAllTextAsync(path, sql, new UTF8Encoding(false), ct);
            return path;
        }
    }
}
