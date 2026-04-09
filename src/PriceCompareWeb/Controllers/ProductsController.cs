using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceCompareCore.Exceptions;
using PriceCompareData.Data;
using PriceCompareData.DTOs;

namespace PriceCompareWeb.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly PriceCompareCore.Interfaces.IProductService _productService;
        private readonly ILogger<ProductsController> _logger;
        private readonly AppDbContext _db;
        private readonly PriceCompareCore.Services.IProductDescriptionSearchService _descriptionSearchService;

        public ProductsController(
            PriceCompareCore.Interfaces.IProductService productService,
            ILogger<ProductsController> logger,
            AppDbContext db,
            PriceCompareCore.Services.IProductDescriptionSearchService descriptionSearchService)
        {
            _productService = productService;
            _logger = logger;
            _db = db;
            _descriptionSearchService = descriptionSearchService;
        }

        /// <summary>
        /// Get products with pagination and optional filters.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetProducts([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
            [FromQuery] string? name = null, [FromQuery] int? shopType = null, [FromQuery] int? categoryId = null,
            [FromQuery] bool includePrice = true)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 200) pageSize = 200;

            try
            {
                _logger.LogInformation("GetProducts start page={Page} pageSize={PageSize} includePrice={IncludePrice}", page, pageSize, includePrice);
                var (total, items) = await _productService.GetProductsAsync(page, pageSize, name, shopType, categoryId);
                _logger.LogInformation("GetProducts items={Count}", items.Count);

                var names = items
                    .Where(p => !string.IsNullOrEmpty(p.Name))
                    .Select(p => p.Name!)
                    .Distinct()
                    .ToList();

                var shopTypes = items
                    .Where(p => p.ShopType.HasValue)
                    .Select(p => p.ShopType!.Value)
                    .Distinct()
                    .ToList();

                var priceMap = new System.Collections.Generic.Dictionary<(string Name, int ShopType), (decimal? Price, string? PromoText)>();
                if (includePrice && names.Count > 0 && shopTypes.Count > 0)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        var latestPrices = await _db.PriceHistory
                            .AsNoTracking()
                            .Where(ph => ph.Name != null
                                         && names.Contains(ph.Name!)
                                         && ph.ShopType.HasValue
                                         && shopTypes.Contains(ph.ShopType!.Value))
                            .GroupBy(ph => new { ph.Name, ph.ShopType })
                            .Select(g => g
                                .OrderByDescending(x => x.ScrapedAt)
                                .Select(x => new
                                {
                                    x.Name,
                                    x.ShopType,
                                    CurrentPrice = (decimal?)x.CurrentPrice,
                                    x.PromoText
                                })
                                .FirstOrDefault())
                            .ToListAsync(cts.Token);

                        _logger.LogInformation("PriceHistory rows={Count}", latestPrices.Count);

                        priceMap = latestPrices
                            .Where(k => k != null && k.Name != null && k.ShopType.HasValue)
                            .ToDictionary(
                                k => (Name: k!.Name!, ShopType: k!.ShopType!.GetValueOrDefault()),
                                v => (v!.CurrentPrice, v.PromoText)
                            );
                    }
                    catch (OperationCanceledException ex)
                    {
                        _logger.LogWarning(ex, "PriceHistory query timed out; returning products without price.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "PriceHistory query failed; returning products without price.");
                    }
                }

                var shaped = items.Select(p => new
                {
                    productId = p.ProductId,
                    name = p.Name,
                    shopType = p.ShopType,
                    sizeValue = p.SizeValue,
                    sizeUnit = p.SizeUnit,
                    brand = p.Brand,
                    imageUrl = p.ImageUrl,
                    price = (p.Name != null && p.ShopType.HasValue
                        && priceMap.TryGetValue((p.Name, p.ShopType.Value), out var info)
                        ? info.Price
                        : null),
                    promoText = (p.Name != null && p.ShopType.HasValue
                        && priceMap.TryGetValue((p.Name, p.ShopType.Value), out var promo)
                        ? promo.PromoText
                        : null)
                }).ToList();

                return Ok(new
                {
                    Page = page,
                    PageSize = pageSize,
                    Count = total,
                    Products = shaped
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get products");
                return StatusCode(500, "Failed to get products");
            }
        }

        /// <summary>
        /// Returns price history for a given product name and shop.
        /// </summary>
        [HttpGet("priceHistory")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPriceHistory(
            [FromQuery] string name,
            [FromQuery] int shopType,
            [FromQuery] int? offerType = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("name is required");

            try
            {
                _logger.LogInformation(
                    "GetPriceHistory name={Name} shopType={ShopType} offerType={OfferType}",
                    name, shopType, offerType);

                var query = _db.PriceHistory
                    .AsNoTracking()
                    .Where(p => p.Name == name && p.ShopType == shopType);

                if (offerType.HasValue)
                    query = query.Where(p => p.OfferType == offerType.Value);

                var history = await query
                    .OrderBy(p => p.ScrapedAt)
                    .Select(p => new
                    {
                        scrapedAt    = p.ScrapedAt,
                        currentPrice = p.CurrentPrice,
                        promoText    = p.PromoText
                    })
                    .ToListAsync();

                _logger.LogInformation(
                    "GetPriceHistory returned {Count} records for name={Name} shopType={ShopType}",
                    history.Count, name, shopType);

                return Ok(history);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get price history for name={Name} shopType={ShopType}", name, shopType);
                return StatusCode(500, "Failed to get price history");
            }
        }

        /// <summary>
        /// POST /api/Products/search-by-description
        /// Natural language product search via LLM keyword extraction.
        /// Rate limited to 3 LLM calls per 24 hours per user (or per IP if anonymous).
        /// </summary>
        [HttpPost("search-by-description")]
        public async Task<IActionResult> SearchByDescription([FromBody] DescriptionSearchRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return BadRequest(new { error = "query_required", message = "Query is required." });

            if (request.Query.Length > 500)
                return BadRequest(new { error = "query_too_long", message = "Query must be 500 characters or fewer." });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var callerId = !string.IsNullOrEmpty(userId)
                ? $"user:{userId}"
                : $"ip:{HttpContext.Connection.RemoteIpAddress}";

            try
            {
                var result = await _descriptionSearchService.SearchAsync(request, callerId);
                return Ok(result);
            }
            catch (AiSearchRateLimitExceededException)
            {
                return StatusCode(429, new
                {
                    error   = "daily_limit_exceeded",
                    message = "You have used all 3 free AI searches for today. Try again tomorrow."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SearchByDescription for callerId={CallerId}", callerId);
                return StatusCode(500, new { error = "internal_error", message = "An unexpected error occurred." });
            }
        }
    }
}
