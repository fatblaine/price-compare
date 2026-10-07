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

        public ColesScrapeException(string message, int statusCode) : base(message)
        {
            StatusCode = statusCode;
        }

        /// <summary>HTTP status Coles returned, when the failure came from a response.</summary>
        public int? StatusCode { get; }

        /// <summary>5xx and 408 are worth retrying after a pause; anything else is not.</summary>
        public bool IsTransient => StatusCode is >= 500 or 408;
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
