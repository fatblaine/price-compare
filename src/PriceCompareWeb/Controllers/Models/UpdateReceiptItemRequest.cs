using System;

namespace PriceCompareWeb.Controllers.Models
{
    /// <summary>
    /// API request model for updating a single receipt item in bulk update.
    /// </summary>
    public class UpdateReceiptItemRequest
    {
        public int? Id { get; set; }

        public string FinalName { get; set; } = string.Empty;

        public Guid? MatchedProductId { get; set; }
    }
}

