using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceCompareData.Common;
using PriceCompareData.Data;
using PriceCompareData.Entities.Compare;

namespace PriceCompareWeb.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CompareController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        public CompareController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }


        [HttpGet]
        public async Task<IActionResult> Compare([FromQuery] string keyword, [FromQuery] int sourceShop)
        {
            ///
            /// TODO: Implement comparison logic here
            /// 1. Fetch some certain products from the database based on the keyword and sourceShop
            /// 2. Get the most recent prices
            /// 3. Target the same product from the other shop
            /// 4. If not found, try to find similar products
            /// 
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return BadRequest("Keyword cannot be empty.");
            }
            var targetShop = sourceShop == ShopType.COLES ? ShopType.WOOLWORTHS : ShopType.COLES;

            // Fetch products from source shop
            var sourceList = await _dbContext.Products
            .Where(p => p.ShopType == sourceShop && p.Name.Contains(keyword))
            .OrderBy(p => p.Name)
            .Take(20)
            .ToListAsync();

            // Get the most recent prices
            decimal? GetLatestPrice(string name, int shopType)
            {
                return _dbContext.PriceHistory
                .Where(ph => ph.ShopType == shopType && ph.Name == name)
                .OrderByDescending(ph => ph.ScrapedAt)
                .Select(ph => (decimal?)ph.CurrentPrice)
                .FirstOrDefault();
            }

            var results = new List<object>();

            foreach (var s in sourceList)
            {
                List<Product> targets = new();

                if (s.CategoryId != null)
                {
                    // 1. Exact match by category + size
                    var q = _dbContext.Products
                        .Where(p => p.ShopType == targetShop && p.CategoryId == s.CategoryId);

                    if (s.SizeValue.HasValue && !string.IsNullOrWhiteSpace(s.SizeUnit))
                    {
                        var low = s.SizeValue.Value * 0.5m;
                        var high = s.SizeValue.Value * 1.5m;
                        q = q.Where(p => p.SizeUnit == s.SizeUnit && p.SizeValue >= low && p.SizeValue <= high);
                    }

                    targets = await q.OrderBy(p => p.Brand).ThenBy(p => p.Name).Take(10).ToListAsync();

                    // 2. Fallback to category (no name filtering, just take the first few in the category)
                    if (targets.Count == 0)
                    {
                        targets = await _dbContext.Products
                            .Where(p => p.ShopType == targetShop && p.CategoryId == s.CategoryId)
                            .OrderBy(p => p.Name)
                            .Take(10)
                            .ToListAsync();
                    }
                }
                else
                {
                    // 3. Fuzzy match by name (when no category is available)
                    targets = await _dbContext.Products
                        .Where(p => p.ShopType == targetShop && p.Name.Contains(s.Name))
                        .OrderBy(p => p.Name)
                        .Take(10)
                        .ToListAsync();
                }

                var sPrice = GetLatestPrice(s.Name, sourceShop);

                // Map the results to include latest prices
                var mapped = targets.Select(t =>
                {
                    var tPrice = GetLatestPrice(t.Name, targetShop);
                    var pricePerUnit = (tPrice.HasValue && t.SizeValue.HasValue && t.SizeValue > 0)
                        ? tPrice / t.SizeValue
                        : null;

                    return new
                    {
                        t.ProductId,
                        t.Name,
                        t.Brand,
                        t.SizeValue,
                        t.SizeUnit,
                        ShopType = t.ShopType,
                        price = tPrice,
                        pricePerUnit
                    };
                }).ToList();

                results.Add(new
                {
                    source = new
                    {
                        s.ProductId,
                        s.Name,
                        s.Brand,
                        s.SizeValue,
                        s.SizeUnit,
                        ShopType = s.ShopType,
                        price = sPrice,
                        pricePerUnit = (sPrice.HasValue && s.SizeValue.HasValue && s.SizeValue > 0)
                            ? sPrice / s.SizeValue
                            : null
                    },
                    targets = mapped
                });
            }

            return Ok(new { matches = results });
        }
    }
}