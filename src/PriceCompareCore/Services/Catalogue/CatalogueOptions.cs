using System.Collections.Generic;

namespace PriceCompareCore.Services.Catalogue
{
    /// <summary>Configuration section "Catalogue" (BTS-163 next-week catalogue).</summary>
    public sealed class CatalogueOptions
    {
        public const string SectionName = "Catalogue";

        public bool Enabled { get; set; } = true;

        /// <summary>
        /// "Direct": run the SQL against the configured database (Lambda in prod).
        /// "Export": also write it to {ExportDir}/{timestamp}_catalogue_{shop}/import.sql so
        /// tools/run-sql-imports.ps1 can apply it elsewhere (fallback if Lambda cannot reach SaleFinder).
        /// </summary>
        public string Mode { get; set; } = "Direct";

        public string ExportDir { get; set; } = "exports";

        /// <summary>Delay between item-detail requests. P0 measured 150 ms with no throttling.</summary>
        public int ItemDelayMs { get; set; } = 150;

        /// <summary>Fail the run when more than this share of items has no SKU (P0 baseline 5–10%).</summary>
        public double MaxItemsWithoutSkuRatio { get; set; } = 0.4;

        /// <summary>Fail the run when more than this share of item-detail requests fails.</summary>
        public double MaxDetailFailureRatio { get; set; } = 0.1;

        /// <summary>Keyed by shop type: 0 Coles, 1 Woolworths.</summary>
        public Dictionary<int, CatalogueRetailerOptions> Retailers { get; set; } = new()
        {
            [1] = new CatalogueRetailerOptions
            {
                RetailerId = 126,
                StoreId = "4777",            // NSW, same region as the existing WWS scrapes
                SaleNameContains = "Weekly Catalogue",
                SaleGroup = "0",
            },
            [0] = new CatalogueRetailerOptions
            {
                RetailerId = 148,
                StoreId = "8738",            // Coles Melbourne Central (VIC METRO), same as the Coles scrapes
                SaleNameContains = "Coles Catalogue",
                SaleGroup = "97",
                IncludeSaleGroup = "90",
            },
        };
    }

    public sealed class CatalogueRetailerOptions
    {
        /// <summary>SaleFinder retailer id: Woolworths 126, Coles 148.</summary>
        public int RetailerId { get; set; }

        /// <summary>SaleFinder store id; decides the region (and so the catalogue edition).</summary>
        public string StoreId { get; set; } = "";

        /// <summary>Picks the weekly catalogue among the retailer's sales (skips Liquorland, Health &amp; Beauty, events).</summary>
        public string SaleNameContains { get; set; } = "";

        public string? SaleGroup { get; set; }
        public string? IncludeSaleGroup { get; set; }

        /// <summary>Optional fixed API key; empty = read the public front-end key at run time.</summary>
        public string? ApiKey { get; set; }
    }
}
