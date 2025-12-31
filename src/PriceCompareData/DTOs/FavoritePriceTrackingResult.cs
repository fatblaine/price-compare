using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class FavoritePriceTrackingResult
    {
        public int Checked { get; set; }
        public int WithPrice { get; set; }
        public int Initialized { get; set; }
        public int Sent { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public int NoPrice { get; set; }
        public int MissingProductOrUser { get; set; }
    }
}