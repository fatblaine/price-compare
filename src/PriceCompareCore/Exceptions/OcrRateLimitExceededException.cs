namespace PriceCompareCore.Exceptions
{
    public class OcrRateLimitExceededException : Exception
    {
        public OcrRateLimitExceededException() : base("OCR rate limit exceeded for today.")
        {
        }
    }
}
