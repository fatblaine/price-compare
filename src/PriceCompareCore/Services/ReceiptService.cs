using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PriceCompareCore.Interfaces;
using PriceCompareData.Data;
using PriceCompareData.DTOs;
using PriceCompareData.Entities;
using PriceCompareData.Entities.Receipts;

namespace PriceCompareCore.Services
{
    public class ReceiptService : IReceiptService
    {
        private readonly AppDbContext _db;
        private sealed record ProductImageLookup(
            Guid ProductId,
            string? ImageUrl,
            string? Name,
            int? ShopType);
        public ReceiptService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<int> CreateReceiptAsync(ReceiptDto dto, Guid userId)
        {
            var receipt = new Receipt
            {
                StoreName = dto.StoreName,
                PurchaseDate = dto.PurchaseDate,
                TotalAmount = dto.TotalAmount,
                UploadUrl = dto.UploadUrl,
                UserId = userId
            };
            _db.Receipts.Add(receipt);
            await _db.SaveChangesAsync();
            return receipt.Id;
        }

        public async Task<ReceiptDetailDto?> GetReceiptAsync(int id, Guid userId)
        {
            var receipt = await _db.Receipts
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
            if (receipt == null)
            {
                return null;
            }
            var matchedProductIds = receipt.Items
                .Where(i => i.MatchedProductId.HasValue)
                .Select(i => i.MatchedProductId!.Value)
                .Distinct()
                .ToList();
            var products = matchedProductIds.Count == 0
                ? new List<ProductImageLookup>()
                : await _db.Products
                    .AsNoTracking()
                    .Where(p => matchedProductIds.Contains(p.ProductId))
                    .Select(p => new ProductImageLookup(
                        p.ProductId,
                        p.ImageUrl,
                        p.Name,
                        p.ShopType))
                    .ToListAsync();
            var imageByProductId = products
                .Where(p => !string.IsNullOrWhiteSpace(p.ImageUrl))
                .ToDictionary(p => p.ProductId, p => p.ImageUrl);
            var missingImageProducts = products
                .Where(p => string.IsNullOrWhiteSpace(p.ImageUrl) && !string.IsNullOrWhiteSpace(p.Name))
                .ToList();
            if (missingImageProducts.Count > 0)
            {
                var missingNames = missingImageProducts
                    .Select(p => p.Name!)
                    .Distinct()
                    .ToList();
                var historyMatches = await _db.PriceHistory
                    .AsNoTracking()
                    .Where(h => h.Name != null && missingNames.Contains(h.Name))
                    .OrderByDescending(h => h.ScrapedAt)
                    .ToListAsync();

                foreach (var product in missingImageProducts)
                {
                    var match = historyMatches.FirstOrDefault(h =>
                        string.Equals(h.Name, product.Name, StringComparison.OrdinalIgnoreCase) &&
                        (!product.ShopType.HasValue || h.ShopType == product.ShopType));
                    if (match != null && !string.IsNullOrWhiteSpace(match.ImageUrl))
                    {
                        imageByProductId[product.ProductId] = match.ImageUrl;
                    }
                }
            }
            return new ReceiptDetailDto(
                receipt.Id,
                receipt.StoreName,
                receipt.PurchaseDate,
                receipt.TotalAmount,
                receipt.UploadUrl,
                receipt.Items.Select(i => new ReceiptItemDto(
                    i.Id,
                    i.ReceiptId,
                    i.ProductName,
                    i.OriginalName,
                    i.MatchedProductId,
                    i.MatchedProductId.HasValue &&
                    imageByProductId.TryGetValue(i.MatchedProductId.Value, out var imageUrl)
                        ? imageUrl
                        : null,
                    i.Confidence
                )).ToList()
            );
        }

        public async Task<IEnumerable<ReceiptDto>> GetReceiptsAsync(Guid userId)
        {
            var receipts = await _db.Receipts.Where(r => r.UserId == userId).ToArrayAsync();
            var result = new List<ReceiptDto>();
            foreach (var receipt in receipts)
            {
                result.Add(new ReceiptDto(
                    receipt.Id,
                    receipt.StoreName,
                    receipt.PurchaseDate,
                    receipt.TotalAmount,
                    receipt.UploadUrl
                ));
            }
            return result;
        }

        public async Task UpdateReceiptItemsAsync(
            int receiptId,
            Guid userId,
            IEnumerable<UpdateReceiptItemModel> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var itemModels = items.ToList();

            // Load receipt with items and ensure it belongs to the current user.
            var receipt = await _db.Receipts
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == receiptId);

            if (receipt == null)
            {
                throw new InvalidOperationException("Receipt not found");
            }

            if (receipt.UserId != userId)
            {
                throw new UnauthorizedAccessException("Receipt does not belong to current user");
            }

            var existingItems = receipt.Items.ToList();
            var existingById = existingItems.ToDictionary(i => i.Id, i => i);
            var keptExistingIds = new HashSet<int>();

            foreach (var model in itemModels)
            {
                if (model == null)
                {
                    continue;
                }

                var finalName = model.FinalName?.Trim();
                if (string.IsNullOrWhiteSpace(finalName))
                {
                    // Empty names are ignored; callers should validate on the client side.
                    continue;
                }

                if (model.Id.HasValue && model.Id.Value > 0)
                {
                    // Update existing item.
                    if (!existingById.TryGetValue(model.Id.Value, out var entity))
                    {
                        throw new InvalidOperationException(
                            $"Receipt item {model.Id.Value} does not belong to this receipt");
                    }

                    entity.ProductName = finalName;
                    entity.MatchedProductId = model.MatchedProductId;
                    // OriginalName and Confidence are preserved for existing records.

                    keptExistingIds.Add(entity.Id);
                }
                else
                {
                    // Create a new item.
                    var newItem = new ReceiptItem
                    {
                        ReceiptId = receiptId,
                        ProductName = finalName,
                        // For user-created items we can store the same text as OriginalName
                        OriginalName = finalName,
                        MatchedProductId = model.MatchedProductId,
                        Quantity = 1,
                        Price = 0m,
                        // User-confirmed items are treated as high-confidence
                        Confidence = 1.0f
                    };

                    _db.ReceiptItems.Add(newItem);
                }
            }

            // Delete items that were previously present but not included in the updated list.
            var toRemove = existingItems
                .Where(e => !keptExistingIds.Contains(e.Id))
                .ToList();

            if (toRemove.Count > 0)
            {
                _db.ReceiptItems.RemoveRange(toRemove);
            }

            await _db.SaveChangesAsync();
        }
    }
}
