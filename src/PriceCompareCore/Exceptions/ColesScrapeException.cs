using System;

namespace PriceCompareCore.Exceptions
{
    /// <summary>
    /// A Coles category scrape did not produce usable data. Thrown so Quartz records the run as failed.
    /// </summary>
    public class ColesScrapeException : Exception
    {
        public ColesScrapeException(string message) : base(message)
        {
        }

        public ColesScrapeException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Coles (Imperva) served its bot-block page, or a previous block is still cooling down.
    /// </summary>
    public class ColesBlockedException : ColesScrapeException
    {
        public ColesBlockedException(string message) : base(message)
        {
        }
    }
}
