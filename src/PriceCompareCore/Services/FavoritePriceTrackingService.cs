using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceCompareCore.Config;
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
        private readonly FavoriteAlertSettings _settings;

        private record PendingFavoriteAlert(
            FavoriteItem Favorite,
            string Email,
            string ProductName,
            int ShopType,
            decimal OldPrice,
            decimal NewPrice,
            decimal DropAmount,
            decimal? DropPercent,
            string FavoritesUrl,
            FavoritePriceAlert Alert);

        private string BuildFavoritesUrl()
        {
            var baseUrl = _settings.BaseUrl?.TrimEnd('/') ?? string.Empty;
            var path = _settings.FavoritesPath ?? "/";
            if (!path.StartsWith("/")) path = "/" + path;
            return baseUrl + path;
        }


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
            ILogger<FavoritePriceTrackingService> logger,
            IOptions<FavoriteAlertSettings> settings)
        {
            _db = db;
            _emailSender = emailSender;
            _logger = logger;
            _settings = settings.Value;
        }

        public async Task<FavoritePriceTrackingResult> CheckAndNotifyAsync()
        {
            _logger.LogInformation("FavoritePriceTracking: starting check");
            var result = new FavoritePriceTrackingResult();

            var pendingAlerts = new List<PendingFavoriteAlert>();

            var favorites = await _db.FavoriteItems
                .Where(f => f.IsActive)
                .ToListAsync();

            if (favorites.Count == 0)
            {
                _logger.LogInformation("FavoritePriceTracking: no active favorites, skipping");
                return result;
            }

            _logger.LogInformation("FavoritePriceTracking: loaded {Count} active favorites", favorites.Count);

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

                var dropAmount = oldPrice.Value - latestPrice;
                decimal? dropPercent = null;
                if (oldPrice.Value > 0)
                {
                    dropPercent = (dropAmount / oldPrice.Value) * 100m;
                }

                var favoritesUrl = BuildFavoritesUrl();

                pendingAlerts.Add(new PendingFavoriteAlert(
                    fav,
                    email,
                    product.Name!,
                    product.ShopType.Value,
                    oldPrice.Value,
                    latestPrice,
                    dropAmount,
                    dropPercent,
                    favoritesUrl,
                    alert));

                _db.FavoritePriceAlerts.Add(alert);
            }

            var groups = pendingAlerts.GroupBy(x => x.Email);

            foreach (var group in groups)
            {
                var ordered = group
                    .OrderByDescending(x => x.DropPercent ?? decimal.MinValue)
                    .ThenByDescending(x => x.DropAmount)
                    .ToList();

                var email = group.Key;
                var subject = BuildDigestSubject(ordered);
                var body = BuildDigestBody(ordered);

                try
                {
                    _logger.LogInformation("FavoritePriceTracking: sending digest to {Email} ({ItemCount} items)", email, ordered.Count);
                    await _emailSender.SendAsync(email, subject, body);

                    var now = DateTime.UtcNow;
                    foreach (var item in ordered)
                    {
                        item.Alert.Status = "Sent";
                        item.Favorite.LastNotifiedPrice = item.NewPrice;
                        item.Favorite.LastNotifiedAt = now;
                        result.Sent++;
                    }
                    _logger.LogInformation("FavoritePriceTracking: sent digest to {Email}", email);
                }
                catch (Exception ex)
                {
                    foreach (var item in ordered)
                    {
                        item.Alert.Status = "Failed";
                        item.Alert.ErrorMessage = ex.Message;
                        result.Failed++;
                    }

                    _logger.LogError(ex, "Failed to send price alert digest email to {Email}", email);
                }
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation(
                "FavoritePriceTracking: done. checked={Checked} initialized={Initialized} sent={Sent} failed={Failed} skipped={Skipped} noPrice={NoPrice} missingProductOrUser={Missing}",
                result.Checked, result.Initialized, result.Sent, result.Failed, result.Skipped, result.NoPrice, result.MissingProductOrUser);
            return result;
        }

        private static string BuildDigestSubject(IReadOnlyList<PendingFavoriteAlert> items)
        {
            if (items.Count == 0)
            {
                return "Price drops for your favorites";
            }

            var top = items[0];
            return $"Price drops for {items.Count} favorites (Top: {top.ProductName})";
        }

        private static string BuildDigestBody(IReadOnlyList<PendingFavoriteAlert> items)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<p>Hello!</p>");
            sb.AppendLine($"<p>{items.Count} of your favorites dropped in price.</p>");

            var top = items[0];
            var topPercent = top.DropPercent.HasValue ? $"{top.DropPercent.Value:F1}%" : "-";
            sb.AppendLine("<h3>Biggest drop</h3>");
            sb.AppendLine(
                $"<p><strong>{WebUtility.HtmlEncode(top.ProductName)}</strong><br/>" +
                $"Old: {top.OldPrice:F2} -> New: {top.NewPrice:F2}<br/>" +
                $"Drop: {topPercent} ({top.DropAmount:F2})<br/>" +
                $"Shop: {GetShopLabel(top.ShopType)}<br/>" +
                $"<a href=\"{top.FavoritesUrl}\">View favorites</a></p>");

            sb.AppendLine("<hr/>");
            sb.AppendLine("<h4>All price drops</h4>");
            sb.AppendLine("<ul>");
            foreach (var item in items)
            {
                var percent = item.DropPercent.HasValue ? $"{item.DropPercent.Value:F1}%" : "-";
                sb.AppendLine(
                    $"<li><strong>{WebUtility.HtmlEncode(item.ProductName)}</strong> - " +
                    $"Old {item.OldPrice:F2}, New {item.NewPrice:F2}, " +
                    $"Drop {percent} ({item.DropAmount:F2}), " +
                    $"Shop {GetShopLabel(item.ShopType)} - " +
                    $"<a href=\"{item.FavoritesUrl}\">View</a></li>");
            }
            sb.AppendLine("</ul>");

            return sb.ToString();
        }
    }
}
