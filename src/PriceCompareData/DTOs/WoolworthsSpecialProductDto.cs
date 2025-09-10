using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class WoolworthsSpecialProductDto
    {
        public int Stockcode { get; set; }
        public string Barcode { get; set; }
        public string Name { get; set; }
        public string Brand { get; set; }
        public decimal Price { get; set; }
        public decimal? WasPrice { get; set; }
        public decimal? SavingsAmount { get; set; }
        public bool IsOnSpecial { get; set; }
        public decimal? CupPrice { get; set; }
        public string CupString { get; set; }
        public string LargeImageFile { get; set; }
    }
}