using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class ProductMatchUpdateRequest
    {
        public Guid MatchId { get; set; }
        // "same_product" or "comparable"
        public string MatchType { get; set; } = "";

        // Only used when keeping comparable.
        public decimal? Score { get; set; }
    }
}