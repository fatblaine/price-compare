using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.Entities.Receipts
{
    public class FavoriteItem
    {
        public int Id { get; set; }
        public Guid UserId { get; set; }
        public Guid ProductId { get; set; }
        public bool IsActive { get; set; } = true;
        public decimal? TargetPrice { get; set; }
        public bool NotifyOnAnyDrop { get; set; } = true;
        public decimal? LastSeenPrice { get; set; }
        public decimal? LastNotifiedPrice { get; set; }
        public DateTime? LastNotifiedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
