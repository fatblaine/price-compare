using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PriceCompareData.Data;
using PriceCompareData.Entities.Jobs;

namespace PriceCompareCore.Services
{
    public class JobRunRecorder
    {
        public static async Task TryRecordAsync(
            AppDbContext db,
            JobRun run,
            ILogger? logger = null,
            CancellationToken ct = default)
        {
            try
            {
                db.JobRuns.Add(run);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to record job run {JobName} ({Source})", run.JobName, run.Source);
            }
        }
    }
}