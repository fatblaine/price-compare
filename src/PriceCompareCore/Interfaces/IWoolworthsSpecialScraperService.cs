using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.DTOs;
using PriceCompareData.Entities.Scraping;

namespace PriceCompareCore.Interfaces
{
    public interface IWoolworthsSpecialScraperService
    {
        //get all the on-special products prices
        Task<List<WoolworthsSpecialProduct>> GetAllOnSpecialProductsAsync(WoolworthsSpecialProductRequest request);
    }
}