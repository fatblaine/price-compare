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
        private readonly IColesDownDomScraperService _colesDownDomService;
        private readonly IColesMeatSeafoodDomScraperService _colesMeatSeafoodDomService;
        private readonly IColesFruitVegetablesDomScraperService _colesFruitVegetablesDomService;
        private readonly IColesDairyEggsFridgeDomScraperService _colesDairyEggsFridgeDomService;
        private readonly IColesBakeryDomScraperService _colesBakeryDomService;
        private readonly IColesDeliDomScraperService _colesDeliDomService;
        private readonly IColesPantryDomScraperService _colesPantryDomService;
        private readonly IColesDietaryWorldFoodsDomScraperService _colesDietaryWorldFoodsDomService;
        private readonly IColesChipsChocolatesSnacksDomScraperService _colesChipsChocolatesSnacksDomService;
        private readonly IColesDrinksDomScraperService _colesDrinksDomService;
        private readonly IColesLiquorlandDomScraperService _colesLiquorlandDomService;
        private readonly IColesFrozenDomScraperService _colesFrozenDomService;
        private readonly IColesCleaningLaundryDomScraperService _colesCleaningLaundryDomService;
        private readonly IColesHealthBeautyDomScraperService _colesHealthBeautyDomService;
        private readonly IColesBabyDomScraperService _colesBabyDomService;
        private readonly IColesPetDomScraperService _colesPetDomService;
        private readonly IColesHomeGardenDomScraperService _colesHomeGardenDomService;
        private readonly IColesBigPackValueDomScraperService _colesBigPackValueDomService;
        private readonly IColesBonusCreditProductsDomScraperService _colesBonusCreditProductsDomService;
        private readonly IColesDeliverMoreRangeDomScraperService _colesDeliverMoreRangeDomService;
        private readonly ILogger<ScrapingController> _logger;
        private readonly IWoolworthsSpecialScraperService _wscraperService;
        private readonly IWoolworthsLowerShelfDomScraperService _wwsLowerShelfDomService;
        private readonly IWoolworthsEverydayLowPriceDomScraperService _wwsEverydayLowPriceDomService;
        private readonly IWoolworthsHalfPriceDomScraperService _wwsHalfPriceDomService;

        public ScrapingController(IColesDownScraperService scraperService,
        ILogger<ScrapingController> logger,
        IColesSpecialScraperService specialScraperService,
        IColesDownDomScraperService colesDownDomService,
        IColesMeatSeafoodDomScraperService colesMeatSeafoodDomService,
        IColesFruitVegetablesDomScraperService colesFruitVegetablesDomService,
        IColesDairyEggsFridgeDomScraperService colesDairyEggsFridgeDomService,
        IColesBakeryDomScraperService colesBakeryDomService,
        IColesDeliDomScraperService colesDeliDomService,
        IColesPantryDomScraperService colesPantryDomService,
        IColesDietaryWorldFoodsDomScraperService colesDietaryWorldFoodsDomService,
        IColesChipsChocolatesSnacksDomScraperService colesChipsChocolatesSnacksDomService,
        IColesDrinksDomScraperService colesDrinksDomService,
        IColesLiquorlandDomScraperService colesLiquorlandDomService,
        IColesFrozenDomScraperService colesFrozenDomService,
        IColesCleaningLaundryDomScraperService colesCleaningLaundryDomService,
        IColesHealthBeautyDomScraperService colesHealthBeautyDomService,
        IColesBabyDomScraperService colesBabyDomService,
        IColesPetDomScraperService colesPetDomService,
        IColesHomeGardenDomScraperService colesHomeGardenDomService,
        IColesBigPackValueDomScraperService colesBigPackValueDomService,
        IColesBonusCreditProductsDomScraperService colesBonusCreditProductsDomService,
        IColesDeliverMoreRangeDomScraperService colesDeliverMoreRangeDomService,
        IWoolworthsSpecialScraperService wscraperService,
        IWoolworthsLowerShelfDomScraperService wwsLowerShelfDomService,
        IWoolworthsEverydayLowPriceDomScraperService wwsEverydayLowPriceDomService,
        IWoolworthsHalfPriceDomScraperService wwsHalfPriceDomService)
        {
            _scraperService = scraperService;
            _logger = logger;
            _specialScraperService = specialScraperService;
            _colesDownDomService = colesDownDomService;
            _colesMeatSeafoodDomService = colesMeatSeafoodDomService;
            _colesFruitVegetablesDomService = colesFruitVegetablesDomService;
            _colesDairyEggsFridgeDomService = colesDairyEggsFridgeDomService;
            _colesBakeryDomService = colesBakeryDomService;
            _colesDeliDomService = colesDeliDomService;
            _colesPantryDomService = colesPantryDomService;
            _colesDietaryWorldFoodsDomService = colesDietaryWorldFoodsDomService;
            _colesChipsChocolatesSnacksDomService = colesChipsChocolatesSnacksDomService;
            _colesDrinksDomService = colesDrinksDomService;
            _colesLiquorlandDomService = colesLiquorlandDomService;
            _colesFrozenDomService = colesFrozenDomService;
            _colesCleaningLaundryDomService = colesCleaningLaundryDomService;
            _colesHealthBeautyDomService = colesHealthBeautyDomService;
            _colesBabyDomService = colesBabyDomService;
            _colesPetDomService = colesPetDomService;
            _colesHomeGardenDomService = colesHomeGardenDomService;
            _colesBigPackValueDomService = colesBigPackValueDomService;
            _colesBonusCreditProductsDomService = colesBonusCreditProductsDomService;
            _colesDeliverMoreRangeDomService = colesDeliverMoreRangeDomService;
            _wscraperService = wscraperService;
            _wwsLowerShelfDomService = wwsLowerShelfDomService;
            _wwsEverydayLowPriceDomService = wwsEverydayLowPriceDomService;
            _wwsHalfPriceDomService = wwsHalfPriceDomService;
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

        [HttpGet("coles/down-down/dom")]
        public async Task<IActionResult> GetDownDownDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesDownDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles down-down DOM products");
                return StatusCode(500, "Failed to get Coles down-down DOM products");
            }
        }

        [HttpGet("coles/meat-seafood/dom")]
        public async Task<IActionResult> GetMeatSeafoodDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesMeatSeafoodDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles meat & seafood DOM products");
                return StatusCode(500, "Failed to get Coles meat & seafood DOM products");
            }
        }

        [HttpGet("coles/fruit-vegetables/dom")]
        public async Task<IActionResult> GetFruitVegetablesDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesFruitVegetablesDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles fruit & vegetables DOM products");
                return StatusCode(500, "Failed to get Coles fruit & vegetables DOM products");
            }
        }

        [HttpGet("coles/dairy-eggs-fridge/dom")]
        public async Task<IActionResult> GetDairyEggsFridgeDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesDairyEggsFridgeDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles dairy, eggs & fridge DOM products");
                return StatusCode(500, "Failed to get Coles dairy, eggs & fridge DOM products");
            }
        }

        [HttpGet("coles/bakery/dom")]
        public async Task<IActionResult> GetBakeryDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesBakeryDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles bakery DOM products");
                return StatusCode(500, "Failed to get Coles bakery DOM products");
            }
        }

        [HttpGet("coles/deli/dom")]
        public async Task<IActionResult> GetDeliDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesDeliDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles deli DOM products");
                return StatusCode(500, "Failed to get Coles deli DOM products");
            }
        }

        [HttpGet("coles/pantry/dom")]
        public async Task<IActionResult> GetPantryDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesPantryDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles pantry DOM products");
                return StatusCode(500, "Failed to get Coles pantry DOM products");
            }
        }

        [HttpGet("coles/dietary-world-foods/dom")]
        public async Task<IActionResult> GetDietaryWorldFoodsDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesDietaryWorldFoodsDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles dietary & world foods DOM products");
                return StatusCode(500, "Failed to get Coles dietary & world foods DOM products");
            }
        }

        [HttpGet("coles/chips-chocolates-snacks/dom")]
        public async Task<IActionResult> GetChipsChocolatesSnacksDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesChipsChocolatesSnacksDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles chips, chocolates & snacks DOM products");
                return StatusCode(500, "Failed to get Coles chips, chocolates & snacks DOM products");
            }
        }

        [HttpGet("coles/drinks/dom")]
        public async Task<IActionResult> GetDrinksDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesDrinksDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles drinks DOM products");
                return StatusCode(500, "Failed to get Coles drinks DOM products");
            }
        }

        [HttpGet("coles/liquorland/dom")]
        public async Task<IActionResult> GetLiquorlandDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesLiquorlandDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles liquorland DOM products");
                return StatusCode(500, "Failed to get Coles liquorland DOM products");
            }
        }

        [HttpGet("coles/frozen/dom")]
        public async Task<IActionResult> GetFrozenDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesFrozenDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles frozen DOM products");
                return StatusCode(500, "Failed to get Coles frozen DOM products");
            }
        }

        [HttpGet("coles/cleaning-laundry/dom")]
        public async Task<IActionResult> GetCleaningLaundryDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesCleaningLaundryDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles cleaning & laundry DOM products");
                return StatusCode(500, "Failed to get Coles cleaning & laundry DOM products");
            }
        }

        [HttpGet("coles/health-beauty/dom")]
        public async Task<IActionResult> GetHealthBeautyDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesHealthBeautyDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles health & beauty DOM products");
                return StatusCode(500, "Failed to get Coles health & beauty DOM products");
            }
        }

        [HttpGet("coles/baby/dom")]
        public async Task<IActionResult> GetBabyDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesBabyDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles baby DOM products");
                return StatusCode(500, "Failed to get Coles baby DOM products");
            }
        }

        [HttpGet("coles/pet/dom")]
        public async Task<IActionResult> GetPetDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesPetDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles pet DOM products");
                return StatusCode(500, "Failed to get Coles pet DOM products");
            }
        }

        [HttpGet("coles/home-garden/dom")]
        public async Task<IActionResult> GetHomeGardenDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesHomeGardenDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles home & garden DOM products");
                return StatusCode(500, "Failed to get Coles home & garden DOM products");
            }
        }

        [HttpGet("coles/big-pack-value/dom")]
        public async Task<IActionResult> GetBigPackValueDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesBigPackValueDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles big pack value DOM products");
                return StatusCode(500, "Failed to get Coles big pack value DOM products");
            }
        }

        [HttpGet("coles/bonus-credit-products/dom")]
        public async Task<IActionResult> GetBonusCreditProductsDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesBonusCreditProductsDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles bonus credit products DOM products");
                return StatusCode(500, "Failed to get Coles bonus credit products DOM products");
            }
        }

        [HttpGet("coles/deliver-more-range/dom")]
        public async Task<IActionResult> GetDeliverMoreRangeDom([FromQuery] int limit = 0)
        {
            try
            {
                var products = await _colesDeliverMoreRangeDomService.ScrapeAsync(limit, HttpContext.RequestAborted);
                return Ok(new
                {
                    Count = products.Count,
                    Products = products
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Coles deliver more range DOM products");
                return StatusCode(500, "Failed to get Coles deliver more range DOM products");
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
