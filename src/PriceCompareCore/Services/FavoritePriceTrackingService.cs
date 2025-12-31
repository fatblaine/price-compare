using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceCompareCore.Interfaces;
using PriceCompareData.Data;
using PriceCompareData.DTOs;
using PriceCompareData.Entities.Receipts;

namespace PriceCompareCore.Services
{
    public class FavoritePriceTrackingService : IFavoritePriceTrackingService
    {
        private readonly AppDbContext _db;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<FavoritePriceTrackingService> _logger;

        private static readonly TimeSpan NotifyCooldown = TimeSpan.FromHours(6);

        private static string GetShopLabel(int shopType)
        {
            return shopType switch
            {
                0 => "Coles",
                1 => "WWS",
                _ => $"Shop {shopType}"
            };
        }

        public FavoritePriceTrackingService(
            AppDbContext db,
            IEmailSender emailSender,
            ILogger<FavoritePriceTrackingService> logger)
        {
            _db = db;
            _emailSender = emailSender;
            _logger = logger;
        }

        public async Task<FavoritePriceTrackingResult> CheckAndNotifyAsync()
        {
            var result = new FavoritePriceTrackingResult();

            var favorites = await _db.FavoriteItems
                .Where(f => f.IsActive)
                .ToListAsync();

            if (favorites.Count == 0)
            {
                return result;
            }

            var productIds = favorites.Select(f => f.ProductId).Distinct().ToList();
            var userIds = favorites.Select(f => f.UserId).Distinct().ToList();

            var products = await _db.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId))
                .ToListAsync();

            var users = await _db.Users
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToListAsync();

            var productMap = products.ToDictionary(p => p.ProductId, p => p);
            var userEmailMap = users.ToDictionary(u => u.Id, u => u.Email);

            var names = products
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => p.Name!)
                .Distinct()
                .ToList();

            var shopTypes = products
                .Where(p => p.ShopType.HasValue)
                .Select(p => p.ShopType!.Value)
                .Distinct()
                .ToList();

            var priceMap = new Dictionary<(string Name, int ShopType), decimal>();

            if (names.Count > 0 && shopTypes.Count > 0)
            {
                var latestPrices = await _db.PriceHistory
                    .AsNoTracking()
                    .Where(ph => ph.Name != null
                                 && names.Contains(ph.Name!)
                                 && ph.ShopType.HasValue
                                 && shopTypes.Contains(ph.ShopType!.Value))
                    .GroupBy(ph => new { ph.Name, ph.ShopType })
                    .Select(g => new
                    {
                        Name = g.Key.Name!,
                        ShopType = g.Key.ShopType!.Value,
                        CurrentPrice = g
                            .OrderByDescending(x => x.ScrapedAt)
                            .Select(x => x.CurrentPrice)
                            .FirstOrDefault()
                    })
                    .ToListAsync();

                priceMap = latestPrices.ToDictionary(
                    k => (k.Name, k.ShopType),
                    v => v.CurrentPrice
                );
            }

            foreach (var fav in favorites)
            {
                result.Checked++;

                if (!productMap.TryGetValue(fav.ProductId, out var product)
                    || !userEmailMap.TryGetValue(fav.UserId, out var email))
                {
                    result.MissingProductOrUser++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(product.Name) || !product.ShopType.HasValue)
                {
                    result.MissingProductOrUser++;
                    continue;
                }

                var key = (product.Name!, product.ShopType.Value);
                if (!priceMap.TryGetValue(key, out var latestPrice))
                {
                    result.NoPrice++;
                    continue;
                }

                result.WithPrice++;

                var oldPrice = fav.LastSeenPrice;

                if (!oldPrice.HasValue)
                {
                    fav.LastSeenPrice = latestPrice;
                    result.Initialized++;
                    continue;
                }

                var isDrop = latestPrice < oldPrice.Value;
                var meetsTarget = fav.TargetPrice.HasValue && latestPrice <= fav.TargetPrice.Value;

                var shouldNotify = (isDrop && fav.NotifyOnAnyDrop) || (isDrop && meetsTarget);

                fav.LastSeenPrice = latestPrice;

                if (!shouldNotify)
                {
                    result.Skipped++;
                    continue;
                }

                if (fav.LastNotifiedAt.HasValue &&
                    DateTime.UtcNow - fav.LastNotifiedAt.Value < NotifyCooldown)
                {
                    result.Skipped++;
                    continue;
                }

                if (fav.LastNotifiedPrice.HasValue &&
                    latestPrice >= fav.LastNotifiedPrice.Value)
                {
                    result.Skipped++;
                    continue;
                }

                var alert = new FavoritePriceAlert
                {
                    FavoriteItemId = fav.Id,
                    OldPrice = oldPrice.Value,
                    NewPrice = latestPrice,
                    TriggeredAt = DateTime.UtcNow,
                    Status = "Pending"
                };

                try
                {
                    var shopLabel = GetShopLabel(product.ShopType.Value);
                    var subject = $"Good news: {product.Name} is cheaper now";
                    var body = $@"
                    <p>Hello!</p>
                    <p>This is a friendly PriceCompare reminder that a favorite item dropped in price.</p>
                    <p><strong>{product.Name}</strong></p>
                    <p>Old price: {oldPrice.Value:F2}</p>
                    <p>New price: {latestPrice:F2}</p>
                    <p>Shop: {shopLabel}</p>
                    <p>If you no longer want these alerts, you can remove the item from favorites.</p>";

                    await _emailSender.SendAsync(email, subject, body);

                    alert.Status = "Sent";
                    fav.LastNotifiedPrice = latestPrice;
                    fav.LastNotifiedAt = DateTime.UtcNow;
                    result.Sent++;
                }
                catch (Exception ex)
                {
                    alert.Status = "Failed";
                    alert.ErrorMessage = ex.Message;
                    result.Failed++;
                    _logger.LogError(ex, "Failed to send price alert email for favorite {FavoriteId}", fav.Id);
                }

                _db.FavoritePriceAlerts.Add(alert);
            }

            await _db.SaveChangesAsync();
            return result;
        }
    }
}
