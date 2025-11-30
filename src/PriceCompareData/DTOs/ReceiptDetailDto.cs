using System;
using System.Collections.Generic;

namespace PriceCompareData.DTOs
{
    public record ReceiptDetailDto(
        int Id,
        string StoreName,
        DateTime PurchaseDate,
        decimal TotalAmount,
        string? UploadUrl,
        IReadOnlyCollection<ReceiptItemDto> Items
    );
}
