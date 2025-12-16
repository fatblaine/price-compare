using System;

namespace PriceCompareData.DTOs
{
    public record ReceiptItemDto(
        int Id,
        int ReceiptId,
        string ProductName
    );
}
