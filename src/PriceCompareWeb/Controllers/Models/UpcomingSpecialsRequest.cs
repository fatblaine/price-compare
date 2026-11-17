using System;
using System.Collections.Generic;

namespace PriceCompareWeb.Controllers.Models
{
    /// <summary>BTS-163: product ids of one page of cards (at most 50).</summary>
    public class UpcomingSpecialsRequest
    {
        public List<Guid> ProductIds { get; set; } = new List<Guid>();
    }
}
