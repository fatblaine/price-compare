using System;

namespace PriceCompareData.DTOs
{
    public record FavoriteItemDto(
        int Id,
        Guid ProductId,
        string ProductName,
        bool IsActive,
        DateTime CreatedAt
    );
}
