using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class ProductMatchReviewQuery
    {
        public decimal MinScore { get; set; } = 0.5m;
        public decimal MaxScore { get; set; } = 0.9m;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }
}