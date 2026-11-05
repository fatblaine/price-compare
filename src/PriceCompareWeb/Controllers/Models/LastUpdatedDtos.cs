using System;
using System.Collections.Generic;

namespace PriceCompareWeb.Controllers.Models
{
    /// <summary>
    /// Response of GET /api/Products/last-updated: when product prices were last refreshed,
    /// overall and per shop. Times are UTC; null means no products for that shop yet.
    /// </summary>
    public record LastUpdatedDto(
        DateTime? LastUpdatedAt,
        IReadOnlyList<ShopLastUpdatedDto> Shops);

    public record ShopLastUpdatedDto(
        int ShopType,
        string ShopName,
        DateTime? LastUpdatedAt);
}
