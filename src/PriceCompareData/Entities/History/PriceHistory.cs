using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.Entities.History
{
    public class PriceHistory
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal CurrentPrice { get; set; }
        public string ImageUrl { get; set; }
        public DateTime ScrapedAt { get; set; }
    }
}