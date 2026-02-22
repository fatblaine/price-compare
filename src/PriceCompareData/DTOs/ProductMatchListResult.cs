using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class ProductMatchListResult
    {
        // Response wrapper for compare-by results.
        public bool Found { get; set; }
        public string? Reason { get; set; }
        public List<ProductMatchCandidate> Matches { get; set; } = new List<ProductMatchCandidate>();
    }
}