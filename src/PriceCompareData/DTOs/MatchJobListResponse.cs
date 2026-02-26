using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class MatchJobListResponse
    {
        public List<MatchJobListItem> Items { get; set; } = new List<MatchJobListItem>();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}