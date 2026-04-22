using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.Entities.Common
{
    public class WebInfo
    {
        public const string COLES_BASE_URL = "https://www.coles.com.au";
        // Next.js build id used in Coles _next/data URLs. Update this when Coles deploys.
        public const string COLES_NEXT_BUILD_ID = "20260413.1-7b71ff44d92d413d7d1b712b532a2fb8ed643433";
        public const string COLES_NEXT_DATA_BASE = COLES_BASE_URL + "/_next/data/" + COLES_NEXT_BUILD_ID;
    }
}
