using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class ColesSpecialProductRequest
    {
        public string? Name { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public bool IsSponsored { get; set; }
        // The amount saved compared to the original price
        // public string Save { get; set; }
        // rating
        // public string Rating { get; set; }
    }
}