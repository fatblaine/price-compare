using System;
using System.Collections.Generic;

namespace PriceCompareCore.Services.Catalogue
{
    /// <summary>One sale (catalogue edition) from the SaleFinder sales list.</summary>
    public sealed record SaleFinderSale(int SaleId, string SaleName, DateTime PublishDate, DateOnly StartDate, DateOnly EndDate);

    /// <summary>The item list of one sale: region name plus the item HTML.</summary>
    public sealed record SaleFinderItemList(string AreaName, string SaleName, DateOnly StartDate, DateOnly EndDate, string ContentHtml);

    /// <summary>One catalogue item parsed from the list HTML.</summary>
    public sealed record ParsedCatalogueItem(
        string ItemId,
        string Name,
        decimal Price,
        string? PriceUnit,
        decimal? WasPrice,
        decimal? SaveAmount,
        string? PromoText,
        string? UnitPriceText,
        string? ImageUrl);

    /// <summary>A parsed item together with the products (SKUs) it covers.</summary>
    public sealed record CatalogueItemWithSkus(ParsedCatalogueItem Item, IReadOnlyList<string> Skus);

    public sealed class CatalogueFetchResult
    {
        public int ShopType { get; init; }
        public bool Skipped { get; init; }
        public string? Message { get; init; }
        public int? SaleId { get; init; }
        public string? SaleName { get; init; }
        public string? Region { get; init; }
        public DateOnly? ValidFrom { get; init; }
        public DateOnly? ValidTo { get; init; }
        public int Items { get; init; }
        public int ItemsWithSku { get; init; }
        public int Skus { get; init; }
        public int SkusMatched { get; init; }
        public int ItemsMatched { get; init; }
        public int DetailFailures { get; init; }
        public string? ExportPath { get; init; }
    }
}
