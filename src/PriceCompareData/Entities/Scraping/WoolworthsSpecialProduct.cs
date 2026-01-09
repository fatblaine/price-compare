using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.Entities.Scraping
{
    public class WoolworthsSpecialProduct
    {
        public int Stockcode { get; set; }
        public string Barcode { get; set; }
        public string DisplayName { get; set; }
        public string Brand { get; set; }

        public decimal Price { get; set; }

        public decimal? WasPrice { get; set; }
        public decimal? SavingsAmount { get; set; }

        public bool IsOnSpecial { get; set; }

        public decimal? CupPrice { get; set; }
        public string CupString { get; set; }

        public string LargeImageFile { get; set; }

        public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
    }
}