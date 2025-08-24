using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Microsoft.AspNetCore.Mvc;
using PriceCompareCore.Interfaces;

namespace PriceCompareWeb.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScrapingController : ControllerBase
    {
        private readonly IScraperService _scraperService;
        private readonly ILogger<ScrapingController> _logger;

        public ScrapingController(IScraperService scraperService, ILogger<ScrapingController> logger)
        {
            _scraperService = scraperService;
            _logger = logger;
        }

        [HttpGet("down-down/all")]
        public async Task<IActionResult> GetDownDownProducts([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var products = await _scraperService.GetAllDownDownProductsAsync();
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
    }
}