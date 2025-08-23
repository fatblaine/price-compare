using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

        [HttpGet]
        public async Task<IActionResult> GetDownDownProducts()
        {
            try
            {
                var products = await _scraperService.GetAllDownDownProductsAsync();
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
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