using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class Pricing
    {
        public decimal Now { get; set; }
        public decimal Was { get; set; }
        public string Comparable { get; set; }
        public string PromotionType { get; set; }
        public string SpecialType { get; set; }
        public bool OnlineSpecial { get; set; }
        public string OfferDescription { get; set; }
    }
}