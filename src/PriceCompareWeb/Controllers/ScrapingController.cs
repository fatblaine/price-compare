using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Microsoft.AspNetCore.Mvc;
using PriceCompareCore.Interfaces;
using PriceCompareData.DTOs;

namespace PriceCompareWeb.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScrapingController : ControllerBase
    {
        private readonly IColesDownScraperService _scraperService;
        private readonly IColesSpecialScraperService _specialScraperService;
        private readonly ILogger<ScrapingController> _logger;
        private readonly IWoolworthsSpecialScraperService _wscraperService;

        public ScrapingController(IColesDownScraperService scraperService,
        ILogger<ScrapingController> logger,
        IColesSpecialScraperService specialScraperService,
        IWoolworthsSpecialScraperService wscraperService)
        {
            _scraperService = scraperService;
            _logger = logger;
            _specialScraperService = specialScraperService;
            _wscraperService = wscraperService;
        }

        [HttpGet("coles/down-down/all")]
        public async Task<IActionResult> GetDownDownProducts([FromQuery] ColesDownProductRequest request, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var products = await _scraperService.GetAllDownDownProductsAsync(request);
                var pagedProducts = products.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                return Ok(new
                {
                    Page = page,
                    PageSize = pageSize,
                    Count = products.Count,
                    Products = pagedProducts
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Down Down products");
                return StatusCode(500, "Failed to get Down Down products");
            }
        }

        [HttpGet("coles/on-special/all")]
        public async Task<IActionResult> GetOnSpecialProducts([FromQuery] ColesSpecialProductRequest request, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var products = await _specialScraperService.GetAllOnSpecialProductsAsync(request);
                var pagedProducts = products.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                return Ok(new
                {
                    Page = page,
                    PageSize = pageSize,
                    Count = products.Count,
                    Products = pagedProducts
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get on-special products");
                return StatusCode(500, "Failed to get on-special products");
            }
        }

        [HttpGet("woolworths/on-special/all")]
        public async Task<IActionResult> GetWoolworthsOnSpecialProducts([FromQuery] WoolworthsSpecialProductRequest request, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var products = await _wscraperService.GetAllOnSpecialProductsAsync(request);
                var pagedProducts = products.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                return Ok(new
                {
                    Page = page,
                    PageSize = pageSize,
                    Count = products.Count,
                    Products = pagedProducts
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Woolworths on-special products");
                return StatusCode(500, "Failed to get Woolworths on-special products");
            }
        }

        [HttpGet("priceHistory")]
        public async Task<IActionResult> GetPriceHistory([FromQuery] string name, [FromQuery] int offerType, [FromQuery] int shopType)
        {
            try
            {
                var history = await _scraperService.GetPriceHistoryAsync(name, offerType, shopType);
                return Ok(history);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get price history");
                return StatusCode(500, "Failed to get price history");
            }
        }

        [HttpPost("priceHistory/cleanup")]
        public async Task<IActionResult> CleanOldPriceHistory()
        {
            try
            {
                var deleted = await _scraperService.CleanOldPriceHistoryAsync();
                return Ok(new
                {
                    Deleted = deleted
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean old price history");
                return StatusCode(500, "Failed to clean old price history");
            }
        }
    }
}
