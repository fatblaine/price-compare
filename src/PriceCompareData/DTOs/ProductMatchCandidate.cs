using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.Entities.Compare;

namespace PriceCompareData.DTOs
{
    public class ProductMatchCandidate
    {
        // Represents one match candidate with its score and method.
        public Product Target { get; set; } = null!;
        public decimal Score { get; set; }
        public string Method { get; set; } = string.Empty;
        public string? MatchType { get; set; }
    }
}