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

        // get price history in the past 7 days
        Task<List<PriceHistory>> GetPriceHistoryAsync(string name, int offerType, int shopType);

        // delete data every 14 days
        Task CleanOldPriceHistoryAsync();
    }
}