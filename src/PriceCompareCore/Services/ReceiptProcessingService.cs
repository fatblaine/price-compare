using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// <summary>
    /// End-to-end pipeline for handling an uploaded receipt image:
    ///  - validate ownership
    ///  - upload to S3
    ///  - run OCR
    ///  - parse store / date / items
    ///  - perform initial fuzzy product matching
    ///  - persist ReceiptItems (with OriginalName + MatchedProductId)
    /// </summary>
    public class ReceiptProcessingService : IReceiptProcessingService
    {
        private readonly AppDbContext _db;
        private readonly IReceiptStorageService _storage;
        private readonly IReceiptOcrService _ocr;
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

            // 1. Ensure the receipt exists and belongs to the current user
            var receipt = await _db.Receipts.FirstOrDefaultAsync(r => r.Id == receiptId);
            if (receipt == null)
            {
                throw new Exception("Receipt not found");
            }

            if (receipt.UserId != userGuid)
            {
                throw new Exception("Receipt does not belong to current user");
            }

            // 2. Upload file to S3
            string key = await _storage.UploadAsync(file, userId, receiptId);
            receipt.UploadUrl = key;

            // 3. Run OCR
            ReceiptOcrResult ocrResult = await _ocr.AnalyzeAsync(key);
            _logger.LogInformation(
                "OCR returned {LineCount} lines for receipt {ReceiptId}. Sample: {Sample}",
                ocrResult.Lines.Count,
                receiptId,
                string.Join(" | ", ocrResult.Lines.Take(6).Select(l => l.Text)));

            var detailedLines = string.Join(
                Environment.NewLine,
                ocrResult.Lines.Select((l, idx) => $"[{idx}] {l.Confidence:F2}: {l.Text}"));
            _logger.LogInformation(
                "OCR lines for receipt {ReceiptId}:{NewLine}{Lines}",
                receiptId,
                Environment.NewLine,
                detailedLines);

            // 4. Detect store name and purchase date
            string storeName = _parser.TryDetectStoreName(ocrResult.Lines);
            if (!string.IsNullOrEmpty(storeName))
            {
                receipt.StoreName = storeName;
                _logger.LogInformation("Detected store '{Store}' for receipt {ReceiptId}", storeName, receiptId);
            }

            DateTime? purchaseDate = _parser.TryDetectPurchaseDate(ocrResult.Lines);
            if (purchaseDate.HasValue)
            {
                // PostgreSQL timestamp with time zone expects UTC; OCR result has unspecified Kind.
                receipt.PurchaseDate = DateTime.SpecifyKind(purchaseDate.Value, DateTimeKind.Utc);
                _logger.LogInformation(
                    "Detected purchase date {Date} (UTC) for receipt {ReceiptId}",
                    receipt.PurchaseDate,
                    receiptId);
            }

            // 5. Parse items and perform initial fuzzy matching against products
            List<ReceiptItem> parsedItems = _parser.ExtractItems(ocrResult.Lines, storeName);
            var items = new List<ReceiptItem>();

            foreach (var parsed in parsedItems)
            {
                var item = new ReceiptItem
                {
                    // Keep OCR text for later review
                    OriginalName = parsed.ProductName,
                    // Start with OCR name as current display name
                    ProductName = parsed.ProductName,
                    // Quantity / price are not used in downstream flows; keep defaulted values
                    Quantity = 1,
                    Price = 0m,
                    Confidence = parsed.Confidence,
                    MatchedProductId = null
                };

                // Try to match to an existing product by name
                var match = await TryMatchProductByNameAsync(parsed.ProductName);
                if (match != null)
                {
                    item.MatchedProductId = match.Value.ProductId;
                    if (!string.IsNullOrWhiteSpace(match.Value.StandardName))
                    {
                        // Use normalized catalog product name as the "final" name
                        item.ProductName = match.Value.StandardName;
                    }
                }

                items.Add(item);
            }

            _logger.LogInformation(
                "Parsed {ItemCount} items for receipt {ReceiptId}. Sample names: {Sample}",
                items.Count,
                receiptId,
                string.Join(" | ", items.Take(5).Select(i => i.ProductName)));

            // Only replace existing items when we actually parsed some, to avoid wiping data on OCR failures
            if (items.Count > 0)
            {
                var existing = await _db.ReceiptItems
                    .Where(i => i.ReceiptId == receiptId)
                    .ToListAsync();

                _db.ReceiptItems.RemoveRange(existing);

                foreach (var item in items)
                {
                    item.ReceiptId = receiptId;
                    _db.ReceiptItems.Add(item);
                }
            }

            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Try to find the best-matching product for the given OCR name.
        /// Returns ProductId + standard product name when the similarity is above a heuristic threshold; otherwise null.
        /// </summary>
        private async Task<(Guid ProductId, string? StandardName)?> TryMatchProductByNameAsync(string? ocrName)
        {
            if (string.IsNullOrWhiteSpace(ocrName))
            {
                return null;
            }

            var normalizedOcr = NormalizeName(ocrName);
            if (string.IsNullOrWhiteSpace(normalizedOcr) || normalizedOcr.Length < 3)
            {
                return null;
            }

            // 1. Use ILIKE to pull a small candidate set from the database (PostgreSQL specific).
            var candidates = await _db.Products
                .AsNoTracking()
                .Where(p => p.Name != null && EF.Functions.ILike(p.Name, $"%{normalizedOcr}%"))
                .Take(32)
                .ToListAsync();

            if (candidates.Count == 0)
            {
                return null;
            }

            // 2. Score candidates using a simple token Jaccard similarity in memory.
            double bestScore = 0;
            Guid? bestProductId = null;
            string? bestName = null;

            foreach (var product in candidates)
            {
                if (string.IsNullOrWhiteSpace(product.Name))
                {
                    continue;
                }

                var normProduct = NormalizeName(product.Name);
                if (string.IsNullOrWhiteSpace(normProduct))
                {
                    continue;
                }

                double score = ComputeTokenSimilarity(normalizedOcr, normProduct);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestProductId = product.ProductId;
                    bestName = product.Name;
                }
            }

            // If the best match is still too weak, skip automatic linking.
            const double Threshold = 0.35;
            if (!bestProductId.HasValue || bestScore < Threshold)
            {
                return null;
            }

            return (bestProductId.Value, bestName);
        }

        private static string NormalizeName(string input)
        {
            var lower = input.ToLowerInvariant();
            // Keep letters / digits / whitespace; replace other characters with spaces.
            var cleaned = Regex.Replace(lower, @"[^\p{L}\p{Nd}\s]+", " ");
            // Collapse multiple spaces into one.
            return Regex.Replace(cleaned, @"\s+", " ").Trim();
        }

        private static double ComputeTokenSimilarity(string normalizedA, string normalizedB)
        {
            var tokensA = normalizedA.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
            var tokensB = normalizedB.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();

            if (tokensA.Count == 0 || tokensB.Count == 0)
            {
                return 0d;
            }

            var setA = new HashSet<string>(tokensA);
            var setB = new HashSet<string>(tokensB);

            int intersection = setA.Intersect(setB).Count();
            int union = setA.Union(setB).Count();

            if (union == 0)
            {
                return 0d;
            }

            return (double)intersection / union;
        }
    }
}
