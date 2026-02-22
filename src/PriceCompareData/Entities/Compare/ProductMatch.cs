using System;
using System.Text.Json;

namespace PriceCompareData.Entities.Compare
{
    public class ProductMatch
    {
        public Guid Id { get; set; }

        public Guid SourceProductId { get; set; }
        public Guid TargetProductId { get; set; }

        public int SourceShopType { get; set; }
        public int TargetShopType { get; set; }

        public decimal Score { get; set; }
        public string? Method { get; set; }
        public string? MatchType { get; set; }

        public bool IsConfirmed { get; set; }
        // Stores optional evidence payload as JSON.
        public JsonDocument? EvidenceJson { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
