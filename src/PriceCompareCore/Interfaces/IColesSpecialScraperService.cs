using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.Entities;
using PriceCompareData.DTOs;

namespace PriceCompareCore.Interfaces
{
    public interface IColesSpecialScraperService
    {
        //get all the on-special products prices
        Task<List<ColesSpecialProduct>> GetAllOnSpecialProductsAsync(ColesSpecialProductRequest request);
    }
}