using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PriceCompareCore.Interfaces;
using PriceCompareData.Data;
using PriceCompareData.Entities.Receipts;

namespace PriceCompareCore.Services
{
    public class FavoriteService : IFavoriteService
    {
        private readonly AppDbContext _db;

        public FavoriteService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<bool> AddFavoriteAsync(string userId, int productId)
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

        public async Task<IEnumerable<FavoriteItem>> GetFavoritesAsync(string userId)
        {
            return await _db.FavoriteItems
            .Where(f => f.UserId == userId)
            .ToListAsync();
        }

        public async Task<bool> RemoveFavoriteAsync(string userId, int productId)
        {
            var fav = await _db.FavoriteItems
            .FirstOrDefaultAsync(f => f.UserId == userId && f.ProductId == productId);

            if (fav == null)
                return false;

            _db.FavoriteItems.Remove(fav);
            await _db.SaveChangesAsync();
            return true;
        }
    }
}