using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class DescriptionSearchResponse
    {
        /// <summary>Original user query as submitted.</summary>
        public string Query { get; set; } = string.Empty;

        /// <summary>
        /// English product name fragments inferred by the LLM from the user query.
        /// Empty if the LLM failed and fallback direct search was used.
        /// </summary>
        public List<string> InferredProducts { get; set; } = new();

        /// <summary>
        /// How many AI searches this caller has remaining today (0–3).
        /// Decremented after each successful LLM call.
        /// </summary>
        public int RemainingSearches { get; set; }

        /// <summary>Matching products, sorted by relevance score descending.</summary>
        public List<ProductSearchItem> Products { get; set; } = new();
    }

    public class ProductSearchItem
    {
        public Guid ProductId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? ShopType { get; set; }
        public string? Brand { get; set; }
        public decimal? SizeValue { get; set; }
        public string? SizeUnit { get; set; }
        public string? ImageUrl { get; set; }
        public decimal? Price { get; set; }
        public string? PromoText { get; set; }
    }
}