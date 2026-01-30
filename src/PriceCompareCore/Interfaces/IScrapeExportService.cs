using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PriceCompareData.Entities.Compare;
using PriceCompareData.Entities.History;

namespace PriceCompareCore.Interfaces
{
    public interface IScrapeExportService
    {
        Task<ScrapeExportResult?> ExportAsync(ScrapeExportRequest request, CancellationToken ct = default);
    }

    public record ScrapeExportRequest(
        string Source,
        DateTime ExportedAtUtc,
        IReadOnlyList<PriceHistory> PriceHistory,
        IReadOnlyList<Product> Products);

    public record ScrapeExportResult(
        string ExportDir,
        int PriceHistoryRows,
        int ProductRows,
        string? PriceHistoryPath,
        string? ProductPath,
        string ManifestPath);
}
