using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs
{
    public class ColesApiProduct
    {
        public string _type { get; set; }
        public int Id { get; set; }
        public string Name { get; set; }
        public string Brand { get; set; }
        public string Description { get; set; }
        public string Size { get; set; }
        public bool Availability { get; set; }
        public string AvailabilityType { get; set; }
        public List<ImageUri> ImageUris { get; set; }
        public Pricing Pricing { get; set; }
    }
}