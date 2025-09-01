using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.Entities
{
    public class ColesSpecialProduct
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal? OriginalPrice { get; set; }
        public string PricePerUnit { get; set; }
        public string ImageUrl { get; set; }
        public string ProductUrl { get; set; }
        public string WasPriceText { get; set; }
        public bool IsSponsored { get; set; }
        // The amount saved compared to the original price
        // public string Save { get; set; }
        // // rating
        // public string Rating { get; set; }

        public DateTime ScrapedAt { get; set; }
    }
}