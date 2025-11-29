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

        public async Task<int> CreateReceiptAsync(ReceiptDto dto, string userId)
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

        public async Task<ReceiptDto?> GetReceiptAsync(int id, string userId)
        {
            var receipt = await _db.Receipts.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
            if (receipt == null)
            {
                return null;
            }
            return new ReceiptDto(
                receipt.Id,
                receipt.StoreName,
                receipt.PurchaseDate,
                receipt.TotalAmount,
                receipt.UploadUrl
            );
        }

        public async Task<IEnumerable<ReceiptDto>> GetReceiptsAsync(string userId)
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