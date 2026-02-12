using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceCompareData.Data;

namespace PriceCompareWeb.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly PriceCompareCore.Interfaces.IProductService _productService;
        private readonly ILogger<ProductsController> _logger;
        private readonly AppDbContext _db;

        public ProductsController(
            PriceCompareCore.Interfaces.IProductService productService,
            ILogger<ProductsController> logger,
            AppDbContext db)
        {
            _productService = productService;
            _logger = logger;
            _db = db;
        }

        /// <summary>
        /// Get products with pagination and optional filters.
        /// </summary>
        /// <param name="page">1-based page number</param>
        /// <param name="pageSize">items per page</param>
        /// <param name="name">partial match on product name</param>
        /// <param name="shopType">shop type (optional)</param>
        /// <param name="categoryId">category id (optional)</param>
        /// <param name="includePrice">include latest price from history</param>
        [HttpGet]
        public async Task<IActionResult> GetProducts([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
            [FromQuery] string? name = null, [FromQuery] int? shopType = null, [FromQuery] int? categoryId = null,
            [FromQuery] bool includePrice = true)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;

            try
            {
                _logger.LogInformation("GetProducts start page={Page} pageSize={PageSize} includePrice={IncludePrice}", page, pageSize, includePrice);
                var (total, items) = await _productService.GetProductsAsync(page, pageSize, name, shopType, categoryId);
                _logger.LogInformation("GetProducts items={Count}", items.Count);

                // build latest price map from history by (Name, ShopType)
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
    }
}
