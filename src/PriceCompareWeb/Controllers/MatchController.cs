using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using PriceCompareCore.Interfaces;
using PriceCompareData.DTOs;

namespace PriceCompareWeb.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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

        private IActionResult BadRequest(string v)
        {
            throw new NotImplementedException();
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
    }
}