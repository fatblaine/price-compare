using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.DTOs;

namespace PriceCompareCore.Interfaces
{
    public interface IReceiptService
    {
        Task<IEnumerable<ReceiptDto>> GetReceiptsAsync(Guid userId);
        Task<ReceiptDetailDto?> GetReceiptAsync(int id, Guid userId);
        Task<int> CreateReceiptAsync(ReceiptDto dto, Guid userId);
    }
}
