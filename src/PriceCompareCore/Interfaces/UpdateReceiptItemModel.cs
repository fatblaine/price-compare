using System;

namespace PriceCompareCore.Interfaces
{
    /// <summary>
    /// Service-layer model representing one edited receipt item from the client.
    /// </summary>
    public class UpdateReceiptItemModel
    {
        /// <summary>
        /// Existing item id. Null or &lt;= 0 means this is a new item to be added.
        /// </summary>
        public int? Id { get; set; }

        /// <summary>
        /// Final product name after user editing.
        /// </summary>
        public string FinalName { get; set; } = string.Empty;

        /// <summary>
        /// Matched product id from the catalog. Null means no match / user chose not to link.
        /// </summary>
        public Guid? MatchedProductId { get; set; }
    }
}

