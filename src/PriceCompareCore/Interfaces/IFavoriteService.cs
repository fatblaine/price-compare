using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.Entities.Receipts;

namespace PriceCompareCore.Interfaces
{
    public interface IFavoriteService
    {
        Task<IEnumerable<FavoriteItem>> GetFavoritesAsync(Guid userId);
        Task<bool> AddFavoriteAsync(Guid userId, int productId);
        Task<bool> RemoveFavoriteAsync(Guid userId, int productId);
    }
}
