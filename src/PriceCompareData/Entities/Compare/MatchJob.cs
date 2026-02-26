using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.Entities.Compare
{
    public class MatchJob
    {
        // Tracks a batch match job and its progress.
        public Guid Id { get; set; }
        public int SourceShop { get; set; }
        public int TargetShop { get; set; }
        public string Status { get; set; } = "queued";
        public string Mode { get; set; } = "incremental";
        public DateTime? Since { get; set; }

        public int Total { get; set; }
        public int Processed { get; set; }
        public int Matched { get; set; }
        public int Comparable { get; set; }
        public int Failed { get; set; }

        public bool UseLlm { get; set; }
        public int LimitNum { get; set; } = 1000;
        public int TopN { get; set; } = 20;

        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}