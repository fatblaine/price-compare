using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Microsoft.AspNetCore.Mvc;
using PriceCompareCore.Exceptions;
using PriceCompareCore.Interfaces;
using PriceCompareCore.Services;
using PriceCompareData.DTOs;

namespace PriceCompareWeb.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScrapingController : ControllerBase
    {
        private readonly IColesDownScraperService _scraperService;
        private readonly IColesSpecialScraperService _specialScraperService;
        private readonly IColesCategoryScraperService _colesCategoryScraper;
        private readonly ILogger<ScrapingController> _logger;
        private readonly IWoolworthsSpecialScraperService _wscraperService;
        private readonly IWoolworthsLowerShelfDomScraperService _wwsLowerShelfDomService;
        private readonly IWoolworthsEverydayLowPriceDomScraperService _wwsEverydayLowPriceDomService;
        private readonly IWoolworthsHalfPriceDomScraperService _wwsHalfPriceDomService;
        private readonly IWoolworthsBuyMoreSaveMoreDomScraperService _wwsBuyMoreSaveMoreDomService;
        private readonly IWoolworthsSummerPriceDomScraperService _wwsSummerPriceDomService;
        private readonly IWoolworthsAutumnPriceDomScraperService _wwsAutumnPriceDomService;

        public ScrapingController(IColesDownScraperService scraperService,
        ILogger<ScrapingController> logger,
        IColesSpecialScraperService specialScraperService,
        IColesCategoryScraperService colesCategoryScraper,
        IWoolworthsSpecialScraperService wscraperService,
        IWoolworthsLowerShelfDomScraperService wwsLowerShelfDomService,
        IWoolworthsEverydayLowPriceDomScraperService wwsEverydayLowPriceDomService,
        IWoolworthsHalfPriceDomScraperService wwsHalfPriceDomService,
        IWoolworthsBuyMoreSaveMoreDomScraperService wwsBuyMoreSaveMoreDomScraperService,
        IWoolworthsSummerPriceDomScraperService wwsSummerPriceDomScraperService,
        IWoolworthsAutumnPriceDomScraperService wwsAutumnPriceDomScraperService)
        {
            _scraperService = scraperService;
            _logger = logger;
            _specialScraperService = specialScraperService;
            _colesCategoryScraper = colesCategoryScraper;
            _wscraperService = wscraperService;
            _wwsLowerShelfDomService = wwsLowerShelfDomService;
            _wwsEverydayLowPriceDomService = wwsEverydayLowPriceDomService;
            _wwsHalfPriceDomService = wwsHalfPriceDomService;
            _wwsBuyMoreSaveMoreDomService = wwsBuyMoreSaveMoreDomScraperService;
            _wwsSummerPriceDomService = wwsSummerPriceDomScraperService;
            _wwsAutumnPriceDomService = wwsAutumnPriceDomScraperService;
        }

        [HttpGet("coles/down-down/all")]
        [ApiExplorerSettings(IgnoreApi = true)]
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

        /// <summary>
        /// Scrapes one Coles category (any slug in <see cref="ColesCategories"/>) through the shared browser session.
        /// Same routes as the old per-category endpoints, e.g. coles/bakery/dom.
        /// </summary>
        [HttpGet("coles/{slug}/dom")]
        public async Task<IActionResult> GetColesCategoryDom(string slug, [FromQuery] int limit = 0)
        {
            var category = ColesCategories.FindBySlug(slug);
            if (category == null)
            {
                return NotFound($"Unknown Coles category '{slug}'.");
            }

            try
            {
                var products = await _colesCategoryScraper.ScrapeAsync(category, limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (ColesScrapeException ex)
            {
                _logger.LogError(ex, "Failed to scrape Coles {Slug}", slug);
                return StatusCode(500, $"Failed to scrape Coles {slug}: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to scrape Coles {Slug}", slug);
                return StatusCode(500, $"Failed to scrape Coles {slug}");
            }
        }

        [HttpGet("coles/on-special/all")]
        [ApiExplorerSettings(IgnoreApi = true)]
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

        [HttpGet("woolworths/on-special/deprecation")]
        [ApiExplorerSettings(IgnoreApi = true)]
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

        [HttpGet("woolworths/lower-shelf/dom")]
        public async Task<IActionResult> GetWoolworthsLowerShelfDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _wwsLowerShelfDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Woolworths lower-shelf-price DOM products");
                return StatusCode(500, "Failed to get Woolworths lower-shelf-price DOM products");
            }
        }

        [HttpGet("woolworths/everyday-low-price/dom")]
        public async Task<IActionResult> GetWoolworthsEverydayLowPriceDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _wwsEverydayLowPriceDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Woolworths everyday-low-price DOM products");
                return StatusCode(500, "Failed to get Woolworths everyday-low-price DOM products");
            }
        }

        [HttpGet("woolworths/half-price/dom")]
        public async Task<IActionResult> GetWoolworthsHalfPriceDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _wwsHalfPriceDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Woolworths half-price DOM products");
                return StatusCode(500, "Failed to get Woolworths half-price DOM products");
            }
        }

        [HttpGet("woolworths/buy-more-save-more/dom")]
        public async Task<IActionResult> GetWoolworthsBuyMoreSaveMoreDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _wwsBuyMoreSaveMoreDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Woolworths buy-more-save-more DOM products");
                return StatusCode(500, "Failed to get Woolworths buy-more-save-more DOM products");
            }
        }

        [HttpGet("woolworths/summer-price/dom")]
        public async Task<IActionResult> GetWoolworthsSummerPriceDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _wwsSummerPriceDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Woolworths summer-price DOM products");
                return StatusCode(500, "Failed to get Woolworths summer-price DOM products");
            }
        }

        [HttpGet("woolworths/autumn-price/dom")]
        public async Task<IActionResult> GetWoolworthsAutumnPriceDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _wwsAutumnPriceDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new { Count = products.Count, Products = products });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Woolworths autumn-price DOM products");
                return StatusCode(500, "Failed to get Woolworths autumn-price DOM products");
            }
        }

        [HttpGet("priceHistory")]
        public async Task<IActionResult> GetPriceHistory([FromQuery] string name, [FromQuery] int shopType, [FromQuery] int? offerType = null)
        {
            try
            {
                var history = await _scraperService.GetPriceHistoryAsync(name, shopType, offerType);
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
