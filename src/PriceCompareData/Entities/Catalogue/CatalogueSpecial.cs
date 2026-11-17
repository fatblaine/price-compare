using System;

namespace PriceCompareData.Entities.Catalogue
{
    /// <summary>
    /// One item of a supermarket's next-week catalogue (BTS-163), as published on SaleFinder.
    /// Table: catalogue_special (tools/sql/bts163_catalogue_special.sql). Rows are written by
    /// CatalogueFetchService through raw SQL; EF only reads them.
    /// </summary>
    public class CatalogueSpecial
    {
        public long Id { get; set; }
        public int ShopType { get; set; }          // 0 Coles / 1 Woolworths
        public int SaleId { get; set; }            // SaleFinder sale id
        public string Region { get; set; } = default!;   // SaleFinder areaName, e.g. "NSW Specialty"
        public string ItemId { get; set; } = default!;   // SaleFinder data-itemid
        public string Name { get; set; } = default!;
        public decimal Price { get; set; }         // per unit; for multi-buys the bundle price divided by quantity
        public string? PriceUnit { get; set; }     // each | kg | pk | multibuy | …
        public decimal? WasPrice { get; set; }
        public decimal? SaveAmount { get; set; }
        public string? UnitPriceText { get; set; }
        public string? PromoText { get; set; }
        public string? ImageUrl { get; set; }
        public DateOnly ValidFrom { get; set; }    // Sydney date
        public DateOnly ValidTo { get; set; }
        public DateTime ScrapedAt { get; set; }
    }
}
