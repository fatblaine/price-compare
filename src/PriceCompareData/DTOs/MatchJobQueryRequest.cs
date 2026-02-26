using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class MatchJobQueryRequest
    {
        public string? Status { get; set; }

        public DateTime? UpdatedFrom { get; set; }
        public DateTime? UpdatedTo { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        // Sorting; allowed: updatedAt | createdAt.
        public string SortBy { get; set; } = "updatedAt";
        public string SortDir { get; set; } = "desc";
    }
}