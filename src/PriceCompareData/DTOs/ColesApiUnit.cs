using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class ColesApiUnit
    {
        public decimal Price { get; set; }
        public string OfMeasureUnits { get; set; }
        public string OfMeasureType { get; set; }
        public bool IsWeighted { get; set; }
    }
}