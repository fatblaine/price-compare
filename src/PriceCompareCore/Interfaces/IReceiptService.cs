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

        /// <summary>
        /// Replace the items of a receipt with a new set provided by the user.
        /// Only ProductName and MatchedProductId are updated; OriginalName and Confidence are preserved for existing items.
        /// </summary>
        Task UpdateReceiptItemsAsync(
            int receiptId,
            Guid userId,
            IEnumerable<UpdateReceiptItemModel> items);

        /// <summary>
        /// Delete a receipt owned by the given user. Receipt items are removed via cascade.
        /// Returns false if the receipt does not exist or belongs to a different user.
        /// </summary>
        Task<bool> DeleteReceiptAsync(int id, Guid userId);
    }
}
