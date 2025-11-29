using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.Entities.Receipts;

namespace PriceCompareData.Entities
{
    public class Receipt
    {
        public int Id { get; set; }
        public Guid UserId { get; set; }
        public string StoreName { get; set; } = string.Empty;
        public DateTime PurchaseDate { get; set; } = DateTime.UtcNow;
        public string? UploadUrl { get; set; }
        public decimal TotalAmount { get; set; }
        public ICollection<ReceiptItem> Items { get; set; } = new List<ReceiptItem>();
    }
}
