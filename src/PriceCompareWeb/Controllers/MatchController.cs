using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PriceCompareCore.Interfaces;
using PriceCompareData.DTOs;

namespace PriceCompareWeb.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "AdminOnly")]
    public class MatchController : ControllerBase
    {
        private readonly IMatchJobService _matchJobService;

        public MatchController(IMatchJobService matchJobService)
        {
            _matchJobService = matchJobService;
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
    }
}
