namespace PriceCompareWeb.Controllers.Models
{
    public record ScrapeImportSqlRequest(
        string ExportDir,
        string? OutputPath,
        bool IncludeSql = false,
        int? BatchSize = null);

    public record ScrapeImportSqlResult(
        string ExportDir,
        string OutputPath,
        int PriceHistoryRows,
        int ProductRows,
        string? SqlPreview);
}
