using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceCompareData.Data;
using PriceCompareWeb.Controllers.Models;
using PriceCompareWeb.Services;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AdminScheduleService _scheduleService;
    private readonly IScrapeImportSqlService _scrapeImportSqlService;

    public AdminController(
        AppDbContext db,
        AdminScheduleService scheduleService,
        IScrapeImportSqlService scrapeImportSqlService)
    {
        _db = db;
        _scheduleService = scheduleService;
        _scrapeImportSqlService = scrapeImportSqlService;
    }

    /// <summary>
    /// Cheapest possible probe of the AdminOnly policy: the frontend calls this on every
    /// login just to learn whether it is allowed through, so it must not touch the DB.
    /// </summary>
    [HttpGet("whoami")]
    public IActionResult WhoAmI()
    {
        return Ok(new AdminWhoAmIDto(true, User.FindFirstValue(ClaimTypes.Email)));
    }

    [HttpGet("schedules")]
    public async Task<IActionResult> GetSchedules([FromQuery] string? source = null, CancellationToken ct = default)
    {
        var result = await _scheduleService.GetSchedulesAsync(source, ct);
        return Ok(result);
    }

    [HttpGet("health")]
    public async Task<IActionResult> GetHealth([FromQuery] int rangeHours = 24, CancellationToken ct = default)
    {
        if (rangeHours <= 0) rangeHours = 24;
        if (rangeHours > 24 * 30) rangeHours = 24 * 30;

        var from = DateTime.UtcNow.AddHours(-rangeHours);
        var query = _db.JobRuns
            .Where(r => r.StartTime >= from);

        var total = await query.CountAsync(ct);
        var failed = await query.Where(r => r.Status == "fail").CountAsync(ct);

        var recentFailures = await query
            .Where(r => r.Status == "fail")
            .OrderByDescending(r => r.StartTime)
            .Take(5)
            .Select(r => new AdminFailureDto(
                r.JobName,
                r.Source,
                r.StartTime,
                r.ErrorMessage))
            .ToListAsync(ct);

        DateTime? lastFailureAtUtc = recentFailures.Count > 0
            ? recentFailures[0].StartTimeUtc
            : null;

        var dto = new AdminHealthDto(
            rangeHours,
            total,
            failed,
            total - failed,
            total == 0 ? 0 : (double)failed / total,
            lastFailureAtUtc,
            recentFailures);

        return Ok(dto);
    }

    [HttpPost("scrape-import/generate-sql")]
    public async Task<IActionResult> GenerateScrapeSql([FromBody] ScrapeImportSqlRequest request, CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ExportDir))
        {
            return BadRequest("ExportDir is required.");
        }

        var result = await _scrapeImportSqlService.GenerateAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("scrape-import/generate-all-sql")]
    public async Task<IActionResult> GenerateAllScrapeSql(
        [FromQuery] bool skipExisting = true, CancellationToken ct = default)
    {
        var result = await _scrapeImportSqlService.GenerateAllAsync(skipExisting, ct);
        return Ok(result);
    }

    [HttpPost("pricehistory/cleanup")]
    public async Task<IActionResult> CleanPriceHistory(CancellationToken ct = default)
    {
        var threshold = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(-2);
        var deleted = await _db.PriceHistory
            .Where(p => p.ScrapedAt < threshold)
            .ExecuteDeleteAsync(ct);
        return Ok(new { deleted });
    }

    [HttpGet("schedules/{jobName}/runs")]
    public async Task<IActionResult> GetRuns(
        string jobName,
        [FromQuery] string source,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return BadRequest("Query parameter 'source' is required.");
        }

        if (limit <= 0) limit = 50;
        if (limit > 500) limit = 500;

        var runs = await _db.JobRuns
            .Where(r => r.JobName == jobName && r.Source == source)
            .OrderByDescending(r => r.StartTime)
            .Take(limit)
            .Select(r => new JobRunDto(
                r.Id,
                r.ScheduledTime,
                r.StartTime,
                r.EndTime,
                r.Status,
                r.DurationMs,
                r.ErrorMessage,
                r.RequestId,
                r.Environment))
            .ToListAsync(ct);

        return Ok(runs);
    }

    [HttpGet("schedules/{jobName}/stats")]
    public async Task<IActionResult> GetStats(
        string jobName,
        [FromQuery] string source,
        [FromQuery] int rangeHours = 24,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return BadRequest("Query parameter 'source' is required.");
        }

        if (rangeHours <= 0) rangeHours = 24;
        if (rangeHours > 24 * 30) rangeHours = 24 * 30;

        var from = DateTime.UtcNow.AddHours(-rangeHours);
        var query = _db.JobRuns
            .Where(r => r.JobName == jobName && r.Source == source && r.StartTime >= from);

        var total = await query.CountAsync(ct);
        var failed = await query.Where(r => r.Status == "fail").CountAsync(ct);
        var avgDuration = await query.AverageAsync(r => (double?)r.DurationMs, ct) ?? 0;

        var stats = new JobRunStatsDto(
            jobName,
            source,
            rangeHours,
            total,
            failed,
            total - failed,
            total == 0 ? 0 : (double)failed / total,
            (int)Math.Round(avgDuration));

        return Ok(stats);
    }
}

public record JobRunDto(
    long Id,
    DateTime? ScheduledTime,
    DateTime StartTime,
    DateTime? EndTime,
    string Status,
    int? DurationMs,
    string? ErrorMessage,
    string? RequestId,
    string? Environment);

public record JobRunStatsDto(
    string JobName,
    string Source,
    int RangeHours,
    int TotalRuns,
    int FailedRuns,
    int SuccessRuns,
    double FailureRate,
    int AverageDurationMs);
