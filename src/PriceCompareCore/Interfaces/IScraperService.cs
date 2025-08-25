using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.Entities;
using PriceCompareData.DTOs;

namespace PriceCompareCore.Interfaces
{
    public interface IScraperService
    {
        // get all the down-down products prices
        Task<List<ScrapedProduct>> GetAllDownDownProductsAsync(ScrapedProductRequest request);

    }
}