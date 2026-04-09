using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareCore.Exceptions
{
    public class AiSearchRateLimitExceededException : Exception
    {
        public AiSearchRateLimitExceededException() : base("AI search rate limit exceeded for today.")
        {
        }
    }
}