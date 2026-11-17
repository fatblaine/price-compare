using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PriceCompareCore.Utils;
using PriceCompareData.Data;
using PriceCompareWeb.Controllers.Models;

namespace PriceCompareWeb.Controllers
{
    /// <summary>
    /// BTS-163: next-week catalogue prices for a page of products. Returns only catalogues that have
    /// not started yet (validfrom after today in Sydney), so from Wednesday the regular scrape price
    /// takes over without any clean-up job.
    /// </summary>
    [ApiController]
    [Route("api/upcoming-specials")]
    public class UpcomingSpecialsController : ControllerBase
    {
        public const int MaxBatchSize = 50;

        private readonly AppDbContext _db;
        private readonly IConfiguration _configuration;
        private readonly Func<DateOnly> _today;

        public UpcomingSpecialsController(AppDbContext db, IConfiguration configuration)
            : this(db, configuration, SydneyClock.Today)
        {
        }

        public UpcomingSpecialsController(AppDbContext db, IConfiguration configuration, Func<DateOnly> today)
        {
            _db = db;
            _configuration = configuration;
            _today = today;
        }

        /// <summary>
        /// Body: { productIds: [...] }. Response: { results: { "&lt;productId&gt;": UpcomingSpecialDto | null } },
        /// the same shape as POST /api/compare-cached/by-products.
        /// </summary>
        [HttpPost("by-products")]
        public async Task<IActionResult> ByProducts([FromBody] UpcomingSpecialsRequest request, CancellationToken ct = default)
        {
            var ids = (request?.ProductIds ?? new List<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
            {
                return BadRequest("productIds is required.");
            }

            if (ids.Count > MaxBatchSize)
            {
                return BadRequest($"productIds must contain at most {MaxBatchSize} ids.");
            }

            var results = ids.ToDictionary(id => id.ToString(), _ => (UpcomingSpecialDto?)null);

            // Kill switch: off = the UI behaves exactly as before BTS-163.
            if (!(_configuration.GetValue<bool?>("UpcomingSpecials:Enabled") ?? true))
            {
                return Ok(new { results });
            }

            var today = _today();
            var rows = await (
                from k in _db.CatalogueSpecialSkus.AsNoTracking()
                join c in _db.CatalogueSpecials.AsNoTracking()
                    on new { k.ShopType, k.SaleId, k.ItemId } equals new { c.ShopType, c.SaleId, c.ItemId }
                where k.ProductId != null && ids.Contains(k.ProductId.Value) && c.ValidFrom > today
                select new
                {
                    ProductId = k.ProductId!.Value,
                    c.ShopType,
                    c.Name,
                    c.Price,
                    c.PriceUnit,
                    c.WasPrice,
                    c.SaveAmount,
                    c.PromoText,
                    c.UnitPriceText,
                    c.ValidFrom,
                    c.ValidTo,
                }).ToListAsync(ct);

            // A product can appear in more than one item; the earliest catalogue, then the lowest price wins.
            foreach (var group in rows.GroupBy(r => r.ProductId))
            {
                var best = group.OrderBy(r => r.ValidFrom).ThenBy(r => r.Price).First();
                results[group.Key.ToString()] = new UpcomingSpecialDto(
                    best.ShopType,
                    best.Name,
                    best.Price,
                    best.PriceUnit,
                    best.WasPrice,
                    best.SaveAmount,
                    best.PromoText,
                    best.UnitPriceText,
                    best.ValidFrom,
                    best.ValidTo);
            }

            return Ok(new { results });
        }
    }

    public record UpcomingSpecialDto(
        int ShopType,
        string CatalogueName,
        decimal Price,
        string? PriceUnit,
        decimal? WasPrice,
        decimal? SaveAmount,
        string? PromoText,
        string? UnitPriceText,
        DateOnly ValidFrom,
        DateOnly ValidTo);
}
