using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.Entities.Receipts
{
    public class ReceiptItem
    {
        public int Id { get; set; }
        public int ReceiptId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        /// <summary>
        /// Original OCR text for the product name, before any normalization or matching.
        /// </summary>
        public string? OriginalName { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public Guid? MatchedProductId { get; set; }
        public float Confidence { get; set; }

        public Receipt? Receipt { get; set; }
    }
}
