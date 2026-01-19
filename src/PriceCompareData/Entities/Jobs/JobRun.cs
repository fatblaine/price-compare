using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.Entities.Jobs
{
    public class JobRun
    {
        public long Id { get; set; }
        public string JobName { get; set; } = default!;
        public string Source { get; set; } = default!;
        public DateTime? ScheduledTime { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Status { get; set; } = default!;
        public int? DurationMs { get; set; }
        public string? ErrorMessage { get; set; }
        public string? RequestId { get; set; }
        public string? Environment { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}