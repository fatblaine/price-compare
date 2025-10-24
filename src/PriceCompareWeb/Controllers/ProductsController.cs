using System;
using System.Linq;
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

        public ProductsController(PriceCompareCore.Interfaces.IProductService productService, ILogger<ProductsController> logger)
        {
            _productService = productService;
            _logger = logger;
        }

        /// <summary>
        /// Get products with pagination and optional filters.
        /// </summary>
        /// <param name="page">1-based page number</param>
        /// <param name="pageSize">items per page</param>
        /// <param name="name">partial match on product name</param>
        /// <param name="shopType">shop type (optional)</param>
        /// <param name="categoryId">category id (optional)</param>
        [HttpGet]
        public async Task<IActionResult> GetProducts([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
            [FromQuery] string? name = null, [FromQuery] int? shopType = null, [FromQuery] int? categoryId = null)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;

            try
            {
                var (total, items) = await _productService.GetProductsAsync(page, pageSize, name, shopType, categoryId);

                return Ok(new
                {
                    Page = page,
                    PageSize = pageSize,
                    Count = total,
                    Products = items
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
