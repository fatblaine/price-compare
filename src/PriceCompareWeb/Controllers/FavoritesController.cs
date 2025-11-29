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

    private Guid UserId => Guid.Parse(_ctx.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public FavoritesController(IFavoriteService service, IHttpContextAccessor ctx)
    {
        _service = service;
        _ctx = ctx;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var list = await _service.GetFavoritesAsync(UserId);
        return Ok(list);
    }

    [HttpPost("{productId:int}")]
    public async Task<IActionResult> Add(int productId)
    {
        var ok = await _service.AddFavoriteAsync(UserId, productId);

        if (!ok)
            return Conflict("Already in favorites");

        return Ok();
    }

    [HttpDelete("{productId:int}")]
    public async Task<IActionResult> Remove(int productId)
    {
        var ok = await _service.RemoveFavoriteAsync(UserId, productId);

        if (!ok)
            return NotFound();

        return NoContent();
    }
}
