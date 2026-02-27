using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceCompareCore.Interfaces;
using PriceCompareData.Common;
using PriceCompareData.Data;
using PriceCompareData.DTOs;

namespace PriceCompareWeb.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "AdminOnly")]
    public class MatchController : ControllerBase
    {
        private readonly IMatchJobService _matchJobService;
        private readonly IMatchLlmReviewService _llmReviewService;
        private readonly AppDbContext _db;

        public MatchController(IMatchJobService matchJobService, IMatchLlmReviewService llmReviewService, AppDbContext db)
        {
            _matchJobService = matchJobService;
            _llmReviewService = llmReviewService;
            _db = db;
        }

        [HttpGet("productmatches/review")]
        public async Task<IActionResult> ReviewProductMatches([FromQuery] ProductMatchReviewQuery request)
        {
            if (request == null)
            {
                return BadRequest("Request is required.");
            }

            // Normalize paging bounds.
            var page = request.Page < 1 ? 1 : request.Page;
            var pageSize = request.PageSize < 1 ? 1 : request.PageSize;
            pageSize = Math.Min(pageSize, 200);

            var minScore = request.MinScore;
            var maxScore = request.MaxScore;

            if (minScore < 0 || maxScore > 1 || minScore >= maxScore)
            {
                return BadRequest("Invalid score range.");
            }

        var baseQuery =
            from m in _db.ProductMatches.AsNoTracking()
            join s in _db.Products.AsNoTracking()
                on m.SourceProductId equals s.ProductId
            join t in _db.Products.AsNoTracking()
                on m.TargetProductId equals t.ProductId
            where m.Score >= minScore && m.Score < maxScore
            orderby m.Score descending, m.UpdatedAt descending
            select new ProductMatchReviewItem
            {
                        MatchId = m.Id,
                        Score = m.Score,
                        MatchType = m.MatchType,
                        Method = m.Method,
                        UpdatedAt = m.UpdatedAt,

                        SourceProductId = s.ProductId,
                        SourceName = s.Name,
                        SourceShopType = s.ShopType,
                        SourceShopName = ShopName(s.ShopType),
                        SourceImageUrl = s.ImageUrl,

                        TargetProductId = t.ProductId,
                        TargetName = t.Name,
                        TargetShopType = t.ShopType,
                        TargetShopName = ShopName(t.ShopType),
                        TargetImageUrl = t.ImageUrl
                    };

            var total = await baseQuery.CountAsync();

            var items = await baseQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new ProductMatchReviewResponse
            {
                Items = items,
                Total = total,
                Page = page,
                PageSize = pageSize
            });
        }

        [HttpPost("productmatches/update")]
        public async Task<IActionResult> UpdateProductMatch([FromBody] ProductMatchUpdateRequest request)
        {
            if (request == null)
            {
                return BadRequest("Request body is required.");
            }

            if (request.MatchId == Guid.Empty)
            {
                return BadRequest("MatchId is required.");
            }

            var matchType = (request.MatchType ?? "").Trim().ToLowerInvariant();
            if (matchType != "same_product" && matchType != "comparable")
            {
                return BadRequest("MatchType must be 'same_product' or 'comparable'.");
            }

            var match = await _db.ProductMatches.FirstOrDefaultAsync(m => m.Id == request.MatchId);
            if (match == null)
            {
                return NotFound();
            }

            if (matchType == "same_product")
            {
                // Force same_product: score=1.000, method=manual.
                match.MatchType = "same_product";
                match.Score = 1.000m;
                match.Method = "manual";
            }
            else
            {
                // Comparable: allow manual score adjustment only.
                if (!request.Score.HasValue)
                {
                    return BadRequest("Score is required when MatchType is comparable.");
                }

                if (request.Score.Value < 0.5000m || request.Score.Value > 0.9999m)
                {
                    return BadRequest("Score must be between 0.5000 and 0.9999.");
                }

                match.MatchType = "comparable";
                match.Score = request.Score.Value;
                // Do not change method for comparable unless you want to.
            }

            match.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }


        [HttpPost("run")]
        public async Task<IActionResult> Run([FromBody] MatchRunRequest request)
        {
            // Validate basic input.
            if (request == null)
            {
                return BadRequest("Request body is required.");
            }

            if (request.SourceShop == request.TargetShop)
            {
                return BadRequest("SourceShop and TargetShop must be different.");
            }

            var jobId = await _matchJobService.StartAsync(request);
            return Ok(new { jobId });
        }

        [HttpPost("llm-review/run")]
        public async Task<IActionResult> RunLlmReview([FromBody] MatchLlmReviewRequest request)
        {
            if (request == null)
            {
                return BadRequest("Request body is required.");
            }

            if (request.SourceShop == request.TargetShop)
            {
                return BadRequest("SourceShop and TargetShop must be different.");
            }

            if (request.TopN < 1) request.TopN = 1;
            if (request.TopN > 50) request.TopN = 50;

            if (request.Limit < 0) request.Limit = 0;

            var jobId = await _llmReviewService.StartAsync(request);
            return Ok(new { jobId });
        }

        [HttpGet("status/{jobId}")]
        public async Task<IActionResult> Status([FromRoute] Guid jobId)
        {
            if (jobId == Guid.Empty)
            {
                return BadRequest("jobId is required.");
            }

            var job = await _matchJobService.GetAsync(jobId);
            if (job == null)
            {
                return NotFound();
            }

            return Ok(job);
        }

        [HttpGet("jobs")]
        public async Task<IActionResult> Jobs([FromQuery] MatchJobQueryRequest request)
        {
            if (request == null)
            {
                return BadRequest("Request is required.");
            }

            try
            {
                var result = await _matchJobService.SearchAsync(request);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        private static string ShopName(int? shopType)
        {
            // Map shop code to display name.
            return shopType switch
            {
                ShopType.COLES => "Coles",
                ShopType.WOOLWORTHS => "Woolworths",
                _ => "Unknown"
            };
        }
    }
}
