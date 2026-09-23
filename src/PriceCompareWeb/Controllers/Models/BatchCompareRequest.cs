using System;
using System.Collections.Generic;

namespace PriceCompareWeb.Controllers.Models
{
    /// <summary>
    /// Request body for POST /api/compare-cached/by-products — one page of product cards.
    /// </summary>
    public class BatchCompareRequest
    {
        public List<Guid> SourceProductIds { get; set; } = new List<Guid>();

        /// <summary>Candidates considered per source product before picking the best same_product match.</summary>
        public int TopN { get; set; } = 10;
    }
}
