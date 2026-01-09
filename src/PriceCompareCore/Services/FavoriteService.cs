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
            return await (
                from fav in _db.FavoriteItems
                join prod in _db.Products on fav.ProductId equals prod.ProductId into prodJoin
                from prod in prodJoin.DefaultIfEmpty()
                where fav.UserId == userId
                orderby fav.CreatedAt descending
                select new FavoriteItemDto(
                    fav.Id,
                    fav.ProductId,
                    prod != null ? prod.Name : $"Product {fav.ProductId}",
                    fav.IsActive,
                    fav.CreatedAt
                )
            ).ToListAsync();
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
