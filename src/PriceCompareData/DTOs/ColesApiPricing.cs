using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class ColesApiPricing
    {
        public decimal Now { get; set; }
        public decimal? Was { get; set; }
        public decimal? SaveAmount { get; set; }
        public string SaveStatement { get; set; }
        public ColesApiUnit Unit { get; set; }
        public string Comparable { get; set; }
        public string PromotionType { get; set; }
    }
}