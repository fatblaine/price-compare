using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class MatchRunRequest
    {
        public Guid? ResumeJobId { get; set; }
        public int SourceShop { get; set; }
        public int TargetShop { get; set; }
        public string Mode { get; set; } = "incremental";
        public DateTime? Since { get; set; }
        public int Limit { get; set; } = 1000;
        public int TopN { get; set; } = 20;
        public bool UseLlm { get; set; } = false;
        public bool Force { get; set; } = false;
    }
}
