using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.DTOs;

namespace PriceCompareCore.Interfaces
{
    public interface IReceiptService
    {
        Task<IEnumerable<ReceiptDto>> GetReceiptsAsync(string userId);
        Task<ReceiptDto?> GetReceiptAsync(int id, string userId);
        Task<int> CreateReceiptAsync(ReceiptDto dto, string userId);
    }
}