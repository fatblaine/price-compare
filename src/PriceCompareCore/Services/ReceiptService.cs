using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PriceCompareCore.Interfaces;
using PriceCompareData.Data;
using PriceCompareData.DTOs;
using PriceCompareData.Entities;

namespace PriceCompareCore.Services
{
    public class ReceiptService : IReceiptService
    {
        private readonly AppDbContext _db;
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
            return new ReceiptDetailDto(
                receipt.Id,
                receipt.StoreName,
                receipt.PurchaseDate,
                receipt.TotalAmount,
                receipt.UploadUrl,
                receipt.Items.Select(i => new ReceiptItemDto(
                    i.Id,
                    i.ReceiptId,
                    i.ProductName
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
    }
}
