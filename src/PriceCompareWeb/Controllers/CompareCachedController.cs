using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceCompareData.Common;
using PriceCompareData.Data;
using PriceCompareData.DTOs;

namespace PriceCompareWeb.Controllers
{
    [ApiController]
    [Route("api/compare-cached")]
    public class CompareCachedController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public CompareCachedController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Read-only compare (cached). Returns precomputed matches by keyword.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> CompareCached([FromQuery] string keyword, [FromQuery] int sourceShop, [FromQuery] int topN = 10)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return BadRequest("Keyword cannot be empty.");
            }

            if (topN < 1) topN = 1;
            if (topN > 20) topN = 20;

            // Fetch products from source shop
            var sourceList = await _dbContext.Products
                .AsNoTracking()
                .Where(p => p.ShopType == sourceShop && p.Name.Contains(keyword))
                .OrderBy(p => p.Name)
                .Take(20)
                .ToListAsync();

            var results = new List<object>();

            foreach (var s in sourceList)
            {
                var matches = await _dbContext.ProductMatches
                    .AsNoTracking()
                    .Where(m => m.SourceProductId == s.ProductId)
                    .OrderByDescending(m => m.Score)
                    .ThenByDescending(m => m.UpdatedAt)
                    .Take(topN)
                    .ToListAsync();

                var targets = new List<object>();

                foreach (var m in matches)
                {
                    var t = await _dbContext.Products.AsNoTracking()
                        .FirstOrDefaultAsync(p => p.ProductId == m.TargetProductId);

                    if (t == null)
                    {
                        continue;
                    }

                    targets.Add(new
                    {
                        t.ProductId,
                        t.Name,
                        t.Brand,
                        t.SizeValue,
                        t.SizeUnit,
                        ShopType = t.ShopType,
                        matchScore = m.Score,
                        matchMethod = m.Method,
                        matchType = m.MatchType
                    });
                }

                results.Add(new
                {
                    source = new
                    {
                        s.ProductId,
                        s.Name,
                        s.Brand,
                        s.SizeValue,
                        s.SizeUnit,
                        ShopType = s.ShopType
                    },
                    targets
                });
            }

            return Ok(new { matches = results });
        }

        /// <summary>
        /// Read-only compare (cached). Returns precomputed matches by sourceProductId.
        /// </summary>
        [HttpGet("by-product")]
        public async Task<IActionResult> CompareCachedByProduct([FromQuery] Guid sourceProductId, [FromQuery] int topN = 10)
        {
            if (sourceProductId == Guid.Empty)
            {
                return BadRequest("sourceProductId is required.");
            }

            if (topN < 1) topN = 1;
            if (topN > 20) topN = 20;

            var matches = await _dbContext.ProductMatches
                .AsNoTracking()
                .Where(m => m.SourceProductId == sourceProductId)
                .OrderByDescending(m => m.Score)
                .ThenByDescending(m => m.UpdatedAt)
                .Take(topN)
                .ToListAsync();

            if (matches.Count == 0)
            {
                return Ok(new ProductMatchListResult
                {
                    Found = false,
                    Reason = "not_precomputed",
                    Matches = new List<ProductMatchCandidate>()
                });
            }

            var results = new List<ProductMatchCandidate>();

            foreach (var m in matches)
            {
                var target = await _dbContext.Products.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.ProductId == m.TargetProductId);

                if (target == null)
                {
                    continue;
                }

                results.Add(new ProductMatchCandidate
                {
                    Target = target,
                    Score = m.Score,
                    Method = m.Method ?? string.Empty,
                    MatchType = m.MatchType
                });
            }

            return Ok(new ProductMatchListResult
            {
                Found = results.Count > 0,
                Reason = results.Count > 0 ? null : "target_not_found",
                Matches = results
            });
        }
    }
}
