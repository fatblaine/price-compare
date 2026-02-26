using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class ProductMatchReviewResponse
    {
        public List<ProductMatchReviewItem> Items { get; set; } = new List<ProductMatchReviewItem>();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}