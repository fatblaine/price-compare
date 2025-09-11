using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.Common;

namespace PriceCompareData.Entities.Compare
{
    public class Product
    {
        public Guid ProductId { get; set; } = Guid.NewGuid();
        public ShopType ShopType { get; set; } // COLES / WOOLWORTHS
        public string? SourceId { get; set; }  // Coles: Id; WWS: Barcode
        public string Name { get; set; } = default!;
        public string? Brand { get; set; }
        public decimal? SizeValue { get; set; }   // 2, 500
        public string? SizeUnit { get; set; }     // L, ml, g, kg
        public int? PackageQty { get; set; }      // 6-pack → 6
        public int? CategoryId { get; set; }
        public string? ImageUrl { get; set; }
        public DateTime LastSeenAt { get; set; }
    }
}