using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.Entities.Jobs
{
    public class JobDefinition
    {
        public string JobName { get; set; } = default!;
        public string Source { get; set; } = default!;
        public string ScheduleExpression { get; set; } = default!;
        public string Timezone { get; set; } = default!;
        public bool Enabled { get; set; } = true;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}