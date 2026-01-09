using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class ColesDownProductRequest
    {
        public string? Name { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public bool IsSponsored { get; set; }
    }
}