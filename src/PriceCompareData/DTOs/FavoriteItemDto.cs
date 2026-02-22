using System;

namespace PriceCompareData.DTOs
{
    public record FavoriteItemDto(
        int Id,
        Guid ProductId,
        string ProductName,
        string? ImageUrl,
        bool IsActive,
        DateTime CreatedAt
    );
}
