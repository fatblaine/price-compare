using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using PriceCompareCore.Interfaces;
using PriceCompareData.DTOs;
using PriceCompareWeb.Controllers.Models;

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
        private readonly IDistributedCache _cache;

        private const int OcrDailyLimit = 3;

        public ReceiptsController(IReceiptService service, IHttpContextAccessor context, IReceiptProcessingService processingService, IDistributedCache cache)
        {
            _service = service;
            _context = context;
            _processingService = processingService;
            _cache = cache;
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
            var error = await ValidateFileAsync(file);
            if (error != null) return BadRequest(error);

            await _processingService.ProcessUploadedReceiptAsync(id, CurrentUserId.ToString(), file);

            return Ok(new { message = "Upload and OCR processing started/completed" });
        }

        /// <summary>
        /// One-step endpoint: create a receipt, upload the image, run OCR + parsing + initial product matching,
        /// and return parsed items (including OCR name, final name, matched product id and confidence).
        /// </summary>
        /// <remarks>
        /// This endpoint will:
        ///  1) create a new receipt record for the current user;
        ///  2) upload the provided file to S3 and run OCR + parsing;
        ///  3) persist store / date / items (with OriginalName / MatchedProductId) into the database;
        ///  4) return parsed items plus the new receipt id (and keep a flat productNames array for backward compatibility).
        /// </remarks>
        [HttpPost("upload-and-parse")]
        public async Task<IActionResult> UploadAndParse(IFormFile file)
        {
            var error = await ValidateFileAsync(file);
            if (error != null) return BadRequest(error);

            // Rate-limit: 3 OCR scans per user per 24 h — stored in IDistributedCache (Redis in
            // production) so the counter is shared across all Lambda instances.
            var usageKey = $"ocr:{CurrentUserId}";
            var storedBytes = await _cache.GetAsync(usageKey);
            var currentUsage = storedBytes != null ? BitConverter.ToInt32(storedBytes) : 0;

            if (currentUsage >= OcrDailyLimit)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { error = "daily_limit_exceeded", message = "You have used all 3 free OCR scans for today. Try again tomorrow." });
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

            // Increment counter only after successful processing.
            await _cache.SetAsync(
                usageKey,
                BitConverter.GetBytes(currentUsage + 1),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                });

            // 3. Load the fully processed receipt detail.
            var detail = await _service.GetReceiptAsync(receiptId, CurrentUserId);
            if (detail is null)
            {
                return StatusCode(500, "Failed to load receipt after processing");
            }

            var productNames = detail.Items.Select(i => i.ProductName).ToArray();

            var items = detail.Items.Select(i => new
            {
                receiptItemId = i.Id,
                ocrName = i.OriginalName ?? i.ProductName,
                finalName = i.ProductName,
                matchedProductId = i.MatchedProductId,
                imageUrl = i.ImageUrl,
                confidence = i.Confidence
            }).ToArray();

            return Ok(new
            {
                receiptId = detail.Id,
                productNames,
                items
            });
        }

        private static async Task<string?> ValidateFileAsync(IFormFile file)
        {
            const long MaxBytes = 10_485_760; // 10 MB

            if (file == null || file.Length == 0)
                return "File is required";

            if (file.Length > MaxBytes)
                return "File exceeds 10 MB limit";

            // Magic-byte verification — do not trust client-supplied Content-Type
            var header = new byte[8];
            int read;
            using (var stream = file.OpenReadStream())
                read = await stream.ReadAsync(header, 0, header.Length);

            bool isJpeg = read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            bool isPng  = read >= 8
                       && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                       && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;
            bool isPdf  = read >= 4 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46;

            if (!isJpeg && !isPng && !isPdf)
                return "Only jpg/png/pdf are supported";

            return null;
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteReceipt(int id)
        {
            var deleted = await _service.DeleteReceiptAsync(id, CurrentUserId);
            return deleted ? NoContent() : NotFound();
        }

        /// <summary>
        /// Replace the items of a receipt with the list provided by the client.
        /// Existing items are updated when an id is supplied; new items are created when id is null or &lt;= 0;
        /// items that were previously present but not included in the payload are deleted.
        /// </summary>
        [HttpPut("{id:int}/items")]
        public async Task<IActionResult> UpdateItems(int id, [FromBody] List<UpdateReceiptItemRequest> items)
        {
            if (items == null)
            {
                return BadRequest("Items payload is required");
            }

            var models = items.Select(r => r == null
                ? null
                : new UpdateReceiptItemModel
                {
                    Id = r.Id,
                    FinalName = r.FinalName,
                    MatchedProductId = r.MatchedProductId
                }).Where(m => m != null)!;

            await _service.UpdateReceiptItemsAsync(id, CurrentUserId, models!);

            return NoContent();
        }
    }
}
