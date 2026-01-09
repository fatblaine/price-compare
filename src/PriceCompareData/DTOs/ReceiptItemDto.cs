using System;

namespace PriceCompareData.DTOs
{
    public record ReceiptItemDto(
        int Id,
        int ReceiptId,
        string ProductName,
        string? OriginalName,
        Guid? MatchedProductId,
        float Confidence
    );
}
