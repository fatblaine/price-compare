using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.DTOs;

namespace PriceCompareCore.Interfaces
{
    public interface IFavoritePriceTrackingService
    {
        Task<FavoritePriceTrackingResult> CheckAndNotifyAsync();
    }
}