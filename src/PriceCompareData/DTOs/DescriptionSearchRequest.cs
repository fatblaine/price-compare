using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class DescriptionSearchRequest
    {
        public string Query { get; set; } = string.Empty;

        /// <summary>How many products to return. Default 10, max 20.</summary>
        public int TopN { get; set; } = 10;

        /// <summary>Optional shop filter. 0 = Coles, 1 = Woolworths, null = all shops.</summary>
        public int? ShopType { get; set; }
    }
}