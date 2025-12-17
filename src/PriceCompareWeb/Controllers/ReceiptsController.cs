using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
        private readonly IReceiptProcessingService _processingService;

        public ReceiptsController(IReceiptService service, IHttpContextAccessor context, IReceiptProcessingService processingService)
        {
            _service = service;
            _context = context;
            _processingService = processingService;
        }

        private Guid CurrentUserId => Guid.Parse(_context.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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

        [HttpPost("{id:int}/upload")]
        public async Task<IActionResult> Upload(int id, IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("File is required");
            }

            // 简单类型检查
            string contentType = file.ContentType;
            if (contentType != "image/jpeg" &&
                contentType != "image/png" &&
                contentType != "application/pdf")
            {
                return BadRequest("Only jpg/png/pdf are supported");
            }

            await _processingService.ProcessUploadedReceiptAsync(id, CurrentUserId.ToString(), file);

            return Ok(new { message = "Upload and OCR processing started/completed" });
        }

        /// <summary>
        /// One-step endpoint: create a receipt, upload the image, run OCR + parsing,
        /// and return parsed product names for the client to filter on.
        /// </summary>
        /// <remarks>
        /// This endpoint will:
        ///  1) create a new receipt record for the current user;
        ///  2) upload the provided file to S3 and run OCR + parsing;
        ///  3) persist store / date / items into the database;
        ///  4) return only the parsed product names (plus the new receipt id).
        /// </remarks>
        [HttpPost("upload-and-parse")]
        public async Task<IActionResult> UploadAndParse(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("File is required");
            }

            string contentType = file.ContentType;
            if (contentType != "image/jpeg" &&
                contentType != "image/png" &&
                contentType != "application/pdf")
            {
                return BadRequest("Only jpg/png/pdf are supported");
            }

            // 1. Create a basic receipt record for the current user.
            //    Store name / purchase date will be filled by OCR later.
            var dto = new ReceiptDto(
                Id: 0,
                StoreName: string.Empty,
                PurchaseDate: DateTime.UtcNow,
                TotalAmount: 0m,
                UploadUrl: null
            );

            var receiptId = await _service.CreateReceiptAsync(dto, CurrentUserId);

            // 2. Upload and process the image (S3 + OCR + parse + items persistence).
            await _processingService.ProcessUploadedReceiptAsync(receiptId, CurrentUserId.ToString(), file);

            // 3. Load the fully processed receipt detail.
            var detail = await _service.GetReceiptAsync(receiptId, CurrentUserId);
            if (detail is null)
            {
                return StatusCode(500, "Failed to load receipt after processing");
            }

            var productNames = detail.Items.Select(i => i.ProductName).ToArray();

            return Ok(new
            {
                receiptId = detail.Id,
                productNames
            });
        }
    }
}
