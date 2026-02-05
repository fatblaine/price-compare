namespace PriceCompareCore.Config
{
    public class ScrapeExportOptions
    {
        public bool Enabled { get; set; } = false;
        public string ExportDir { get; set; } = "exports";
        public int BatchSize { get; set; } = 500;
    }
}
