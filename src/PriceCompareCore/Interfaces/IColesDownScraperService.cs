using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.Entities;
using PriceCompareData.DTOs;
using PriceCompareData.Entities.History;

namespace PriceCompareCore.Interfaces
{
    public interface IColesDownScraperService
    {
        // get all the down-down products prices
        Task<List<ColesDownProduct>> GetAllDownDownProductsAsync(ColesDownProductRequest request);

        // get price history (optionally filter by offer type)
        Task<List<PriceHistory>> GetPriceHistoryAsync(string name, int shopType, int? offerType = null);

        // delete old price history records
        Task<int> CleanOldPriceHistoryAsync();
    }
}
