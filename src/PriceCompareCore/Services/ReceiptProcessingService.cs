using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceCompareCore.Interfaces;
using PriceCompareData.Data;
using PriceCompareData.Entities;
using PriceCompareData.Entities.Receipts;

namespace PriceCompareCore.Services
{
    public class ReceiptProcessingService : IReceiptProcessingService
    {
        // Database context
        private readonly AppDbContext _db;
        // S3 storage service
        private readonly IReceiptStorageService _storage;
        // OCR service
        private readonly IReceiptOcrService _ocr;
        // Receipt parser
        private readonly ReceiptOcrParser _parser;
        private readonly ILogger<ReceiptProcessingService> _logger;

        public ReceiptProcessingService(
            AppDbContext db,
            IReceiptStorageService storage,
            IReceiptOcrService ocr,
            ILogger<ReceiptProcessingService> logger)
        {
            _db = db;
            _storage = storage;
            _ocr = ocr;
            _parser = new ReceiptOcrParser();
            _logger = logger;
        }

        public async Task ProcessUploadedReceiptAsync(int receiptId, string userId, IFormFile file)
        {
            if (!Guid.TryParse(userId, out var userGuid))
            {
                throw new ArgumentException("Invalid user id format", nameof(userId));
            }

            // 1. 检查这个 receipt 是不是属于当前用户
            Receipt receipt = await _db.Receipts.FirstOrDefaultAsync(r => r.Id == receiptId);

            if (receipt == null)
            {
                throw new Exception("Receipt not found");
            }

            if (receipt.UserId != userGuid)
            {
                throw new Exception("Receipt does not belong to current user");
            }

            // 2. 上传到 S3
            string key = await _storage.UploadAsync(file, userId, receiptId);
            receipt.UploadUrl = key;

            // 3. 调用 Rekognition 做 OCR
            ReceiptOcrResult ocrResult = await _ocr.AnalyzeAsync(key);
            _logger.LogInformation("OCR returned {LineCount} lines for receipt {ReceiptId}. Sample: {Sample}",
                ocrResult.Lines.Count,
                receiptId,
                string.Join(" | ", ocrResult.Lines.Take(6).Select(l => l.Text)));
            var detailedLines = string.Join(Environment.NewLine,
                ocrResult.Lines.Select((l, idx) => $"[{idx}] {l.Confidence:F2}: {l.Text}"));
            _logger.LogInformation("OCR lines for receipt {ReceiptId}:{NewLine}{Lines}",
                receiptId,
                Environment.NewLine,
                detailedLines);

            // 4. 解析店名和日期
            string storeName = _parser.TryDetectStoreName(ocrResult.Lines);
            if (!String.IsNullOrEmpty(storeName))
            {
                receipt.StoreName = storeName;
                _logger.LogInformation("Detected store '{Store}' for receipt {ReceiptId}", storeName, receiptId);
            }

            DateTime? purchaseDate = _parser.TryDetectPurchaseDate(ocrResult.Lines);
            if (purchaseDate.HasValue)
            {
                // PostgreSQL timestamp with time zone requires UTC; OCR 解析得到的时间没有 Kind，显式标为 Utc
                receipt.PurchaseDate = DateTime.SpecifyKind(purchaseDate.Value, DateTimeKind.Utc);
                _logger.LogInformation("Detected purchase date {Date} (UTC) for receipt {ReceiptId}", receipt.PurchaseDate, receiptId);
            }

            // 5. 解析商品行
            List<ReceiptItem> items = _parser.ExtractItems(ocrResult.Lines);
            _logger.LogInformation("Parsed {ItemCount} items for receipt {ReceiptId}. Sample: {Sample}",
                items.Count,
                receiptId,
                string.Join(" | ", items.Take(5).Select(i => $"{i.ProductName} {i.Quantity} x {i.Price}")));

            // 仅当解析到新商品时才替换旧商品，避免 OCR 失败时把已有数据清空
            if (items.Count > 0)
            {
                _db.ReceiptItems.RemoveRange(
                    await _db.ReceiptItems
                        .Where(i => i.ReceiptId == receiptId)
                        .ToListAsync());

                foreach (var item in items)
                {
                    item.ReceiptId = receiptId;
                    _db.ReceiptItems.Add(item);
                }
            }

            await _db.SaveChangesAsync();
        }
    }
}
