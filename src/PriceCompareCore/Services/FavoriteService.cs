using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PriceCompareCore.Interfaces;
using PriceCompareData.Data;
using PriceCompareData.Entities.Receipts;
using PriceCompareData.DTOs;

namespace PriceCompareCore.Services
{
    public class FavoriteService : IFavoriteService
    {
        private readonly AppDbContext _db;
        private sealed record FavoriteProductLookup(
            int FavoriteId,
            Guid ProductId,
            string ProductName,
            string? ImageUrl,
            int? ShopType,
            bool IsActive,
            DateTime CreatedAt);

        public FavoriteService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<bool> AddFavoriteAsync(Guid userId, Guid productId)
        {
            var exists = await _db.FavoriteItems
            .AnyAsync(f => f.UserId == userId && f.ProductId == productId);

            if (exists)
                return false;

            _db.FavoriteItems.Add(new FavoriteItem
            {
                UserId = userId,
                ProductId = productId
            });

            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<IEnumerable<FavoriteItemDto>> GetFavoritesAsync(Guid userId)
        {
            var baseList = await (
                from fav in _db.FavoriteItems
                join prod in _db.Products on fav.ProductId equals prod.ProductId into prodJoin
                from prod in prodJoin.DefaultIfEmpty()
                where fav.UserId == userId
                orderby fav.CreatedAt descending
                select new FavoriteProductLookup(
                    fav.Id,
                    fav.ProductId,
                    prod != null ? prod.Name : $"Product {fav.ProductId}",
                    prod != null ? prod.ImageUrl : null,
                    prod != null ? prod.ShopType : null,
                    fav.IsActive,
                    fav.CreatedAt
                )
            ).ToListAsync();

            var missing = baseList
                .Where(x => string.IsNullOrWhiteSpace(x.ImageUrl))
                .Where(x => !string.IsNullOrWhiteSpace(x.ProductName))
                .ToList();

            var imageByFavoriteId = new Dictionary<int, string?>();
            if (missing.Count > 0)
            {
                var names = missing
                    .Select(x => x.ProductName)
                    .Distinct()
                    .ToList();

                var historyMatches = await _db.PriceHistory
                    .AsNoTracking()
                    .Where(h => h.Name != null && names.Contains(h.Name))
                    .OrderByDescending(h => h.ScrapedAt)
                    .ToListAsync();

                foreach (var fav in missing)
                {
                    var match = historyMatches.FirstOrDefault(h =>
                        string.Equals(h.Name, fav.ProductName, StringComparison.OrdinalIgnoreCase) &&
                        (!fav.ShopType.HasValue || h.ShopType == fav.ShopType));
                    if (match != null && !string.IsNullOrWhiteSpace(match.ImageUrl))
                    {
                        imageByFavoriteId[fav.FavoriteId] = match.ImageUrl;
                    }
                }
            }

            return baseList
                .Select(f => new FavoriteItemDto(
                    f.FavoriteId,
                    f.ProductId,
                    f.ProductName,
                    !string.IsNullOrWhiteSpace(f.ImageUrl)
                        ? f.ImageUrl
                        : (imageByFavoriteId.TryGetValue(f.FavoriteId, out var img) ? img : null),
                    f.IsActive,
                    f.CreatedAt
                ))
                .ToList();
        }

        public async Task<bool> RemoveFavoriteAsync(Guid userId, Guid productId)
        {
            var fav = await _db.FavoriteItems
            .FirstOrDefaultAsync(f => f.UserId == userId && f.ProductId == productId);

            if (fav == null)
                return false;

            _db.FavoriteItems.Remove(fav);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> SetFavoriteActiveAsync(Guid userId, Guid productId, bool isActive)
        {
            var fav = await _db.FavoriteItems
                .FirstOrDefaultAsync(f => f.UserId == userId && f.ProductId == productId);

            if (fav == null)
                return false;

            fav.IsActive = isActive;
            await _db.SaveChangesAsync();
            return true;
        }
    }
}
