using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.Entities.Receipts;

namespace PriceCompareCore.Interfaces
{
    public interface IFavoriteService
    {
        Task<IEnumerable<FavoriteItem>> GetFavoritesAsync(string userId);
        Task<bool> AddFavoriteAsync(string userId, int productId);
        Task<bool> RemoveFavoriteAsync(string userId, int productId);
    }
}