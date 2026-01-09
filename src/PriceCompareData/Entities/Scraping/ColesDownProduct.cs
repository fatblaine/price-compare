using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.Entities
{
    public class ColesDownProduct
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal? OriginalPrice { get; set; }
        public string PricePerUnit { get; set; }
        public string ImageUrl { get; set; }
        public string ProductUrl { get; set; }
        public string WasPriceText { get; set; }
        public bool IsSponsored { get; set; }

        public DateTime ScrapedAt { get; set; }
    }
}