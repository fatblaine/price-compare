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
        public const string COLES_NEXT_BUILD_ID = "20260814.4-42e5f7ce808d415777482c42c76722f26b06a428";
        public const string COLES_NEXT_DATA_BASE = COLES_BASE_URL + "/_next/data/" + COLES_NEXT_BUILD_ID;
    }
}
