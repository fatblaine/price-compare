using System;

namespace PriceCompareData.Entities.Catalogue
{
    /// <summary>
    /// A product covered by a catalogue item (BTS-163). One item can cover many products
    /// ("selected varieties"). Sku = Woolworths stockcode / Coles product id; ProductId is filled
    /// by SQL matching in the target database. Table: catalogue_special_sku.
    /// </summary>
    public class CatalogueSpecialSku
    {
        public int ShopType { get; set; }
        public int SaleId { get; set; }
        public string ItemId { get; set; } = default!;
        public string Sku { get; set; } = default!;
        public Guid? ProductId { get; set; }
    }
}
