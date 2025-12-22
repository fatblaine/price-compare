using System;

namespace PriceCompareData.DTOs
{
    public record FavoriteItemDto(
        int Id,
        Guid ProductId,
        string ProductName,
        DateTime CreatedAt
    );
}

