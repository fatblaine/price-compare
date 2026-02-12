using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.Entities.History
{
    public class PriceHistory
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public decimal CurrentPrice { get; set; }
        public string ImageUrl { get; set; }
        public DateTime ScrapedAt { get; set; }
        public string? PromoText { get; set; }

        // 0: down-down 1: on-special
        public int? OfferType { get; set; }
        // 0: coles 1: woolworths
        public int? ShopType { get; set; }
    }
}
