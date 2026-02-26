using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class ProductMatchReviewItem
    {
        public Guid MatchId { get; set; }
        public decimal Score { get; set; }
        public string? MatchType { get; set; }
        public string? Method { get; set; }
        public DateTime UpdatedAt { get; set; }

        public Guid SourceProductId { get; set; }
        public string SourceName { get; set; } = "";
        public int? SourceShopType { get; set; }
        public string SourceShopName { get; set; } = "";
        public string? SourceImageUrl { get; set; }

        public Guid TargetProductId { get; set; }
        public string TargetName { get; set; } = "";
        public int? TargetShopType { get; set; }
        public string TargetShopName { get; set; } = "";
        public string? TargetImageUrl { get; set; }
    }
}