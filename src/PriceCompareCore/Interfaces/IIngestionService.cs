using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.Entities;
using PriceCompareData.Entities.Scraping;

namespace PriceCompareCore.Interfaces
{
    public interface IIngestionService
    {
        Task UpsertColesSpecialAsync(IEnumerable<ColesSpecialProduct> items);
        Task UpsertColesDownAsync(IEnumerable<ColesDownProduct> items);
        Task UpsertWwsAsync(IEnumerable<WoolworthsSpecialProduct> items);
    }
}