using System;
using System.Collections.Generic;

namespace PriceCompareWeb.Controllers.Models
{
    public record JobScheduleDto(
        string JobName,
        string Source,
        string ScheduleExpression,
        string Timezone,
        bool Enabled,
        string? Description,
        DateTime? NextFireTimeUtc,
        DateTime? LastRunTimeUtc,
        string? LastRunStatus,
        int? LastRunDurationMs,
        string? LastRunErrorMessage);

    public record AdminWhoAmIDto(
        bool IsAdmin,
        string? Email);

    public record AdminFailureDto(
        string JobName,
        string Source,
        DateTime StartTimeUtc,
        string? ErrorMessage);

    public record AdminHealthDto(
        int RangeHours,
        int TotalRuns,
        int FailedRuns,
        int SuccessRuns,
        double FailureRate,
        DateTime? LastFailureAtUtc,
        List<AdminFailureDto> RecentFailures);
}
