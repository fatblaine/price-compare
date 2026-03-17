using System;

namespace PriceCompareCore.Exceptions
{
    public sealed class OtpExpiredException : Exception
    {
        public OtpExpiredException(string message) : base(message) { }
    }

    public sealed class OtpMismatchException : Exception
    {
        public OtpMismatchException(string message) : base(message) { }
    }
}
