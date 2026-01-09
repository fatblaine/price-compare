using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.DTOs;

namespace PriceCompareCore.Interfaces
{
    public interface IFavoriteService
    {
        Task<IEnumerable<FavoriteItemDto>> GetFavoritesAsync(Guid userId);
        Task<bool> AddFavoriteAsync(Guid userId, Guid productId);
        Task<bool> RemoveFavoriteAsync(Guid userId, Guid productId);
        Task<bool> SetFavoriteActiveAsync(Guid userId, Guid productId, bool isActive);
    }
}
