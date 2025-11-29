using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PriceCompareCore.Interfaces;
using PriceCompareData.DTOs;

namespace PriceCompareWeb.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReceiptsController : ControllerBase
    {
        private readonly IReceiptService _service;
        private readonly IHttpContextAccessor _context;

        public ReceiptsController(IReceiptService service, IHttpContextAccessor context)
        {
            _service = service;
            _context = context;
        }

        private string CurrentUserId => _context.HttpContext!.User.Identity!.Name!;

        [HttpGet]
        public async Task<IActionResult> GetMyReceipts()
            => Ok(await _service.GetReceiptsAsync(CurrentUserId));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetReceipt(int id)
        {
            var receipt = await _service.GetReceiptAsync(id, CurrentUserId);
            return receipt is null ? NotFound() : Ok(receipt);
        }

        [HttpPost]
        public async Task<IActionResult> CreateReceipt([FromBody] ReceiptDto dto)
        {
            var id = await _service.CreateReceiptAsync(dto, CurrentUserId);
            return CreatedAtAction(nameof(GetReceipt), new { id }, dto);
        }
    }
}