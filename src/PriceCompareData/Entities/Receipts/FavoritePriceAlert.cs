using System;

namespace PriceCompareData.Entities.Receipts
{
    public class FavoritePriceAlert
    {
        public int Id { get; set; }
        public int FavoriteItemId { get; set; }
        public decimal? OldPrice { get; set; }
        public decimal NewPrice { get; set; }
        public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = "Pending";
        public string? ErrorMessage { get; set; }

        public FavoriteItem? FavoriteItem { get; set; }
    }
}
