using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class MatchJobListItem
    {
        public Guid Id { get; set; }
        public int SourceShop { get; set; }
        public int TargetShop { get; set; }
        public string Status { get; set; } = "";
        public string Mode { get; set; } = "";
        public DateTime? Since { get; set; }

        public int Total { get; set; }
        public int Processed { get; set; }
        public int Matched { get; set; }
        public int Comparable { get; set; }
        public int Failed { get; set; }

        public bool UseLlm { get; set; }
        public int LimitNum { get; set; }
        public int TopN { get; set; }

        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}