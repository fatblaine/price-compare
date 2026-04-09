using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.DTOs;

namespace PriceCompareCore.Services
{
    public interface IProductDescriptionSearchService
    {
        Task<DescriptionSearchResponse> SearchAsync(DescriptionSearchRequest request, string callerId);
    }
}