using System;

namespace PriceCompareCore.Utils
{
    /// <summary>
    /// Today's date in Sydney. Catalogue validity (Wednesday to Tuesday) is a Sydney calendar
    /// thing, and the code runs both on Lambda (UTC) and on the Windows scraping machine.
    /// </summary>
    public static class SydneyClock
    {
        private static readonly Lazy<TimeZoneInfo> Zone = new(FindZone);

        public static TimeZoneInfo TimeZone => Zone.Value;

        public static DateOnly Today() => ToSydneyDate(DateTime.UtcNow);

        public static DateOnly ToSydneyDate(DateTime utc)
        {
            var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(asUtc, Zone.Value));
        }

        private static TimeZoneInfo FindZone()
        {
            // IANA id on Linux / Lambda and on Windows with ICU; Windows id as a fallback.
            foreach (var id in new[] { "Australia/Sydney", "AUS Eastern Standard Time" })
            {
                if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
                {
                    return zone;
                }
            }

            throw new InvalidOperationException("Sydney time zone not found on this machine.");
        }
    }
}
