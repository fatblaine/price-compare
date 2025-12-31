using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PriceCompareCore.Interfaces;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FavoritesController : ControllerBase
{
    private readonly IFavoriteService _service;
    private readonly IHttpContextAccessor _ctx;

    private readonly IFavoritePriceTrackingService _trackingService;

    private Guid UserId => Guid.Parse(_ctx.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public FavoritesController(IFavoriteService service, IHttpContextAccessor ctx, IFavoritePriceTrackingService trackingService)
    {
        _service = service;
        _ctx = ctx;
        _trackingService = trackingService;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var list = await _service.GetFavoritesAsync(UserId);
        return Ok(list);
    }

    [HttpPost("{productId:guid}")]
    public async Task<IActionResult> Add(Guid productId)
    {
        var ok = await _service.AddFavoriteAsync(UserId, productId);

        if (!ok)
            return Conflict("Already in favorites");

        return Ok();
    }

    [HttpDelete("{productId:guid}")]
    public async Task<IActionResult> Remove(Guid productId)
    {
        var ok = await _service.RemoveFavoriteAsync(UserId, productId);

        if (!ok)
            return NotFound();

        return NoContent();
    }

    [HttpPost("price-check")]
    public async Task<IActionResult> PriceCheck()
    {
        var result = await _trackingService.CheckAndNotifyAsync();
        return Ok(result);
    }

}
