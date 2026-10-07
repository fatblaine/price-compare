using System;
using PriceCompareCore.Exceptions;

namespace PriceCompareCore.Services
{
    /// <summary>
    /// Validation shared by the Coles category scrape (BTS-156). Anything that is not usable product JSON
    /// throws <see cref="ColesScrapeException"/>, so a blocked or broken scrape shows up as a failed job
    /// instead of "succeeded with 0 products".
    /// </summary>
    public static class ColesDomFetchGuard
    {
        private static readonly string[] ImpervaMarkers = { "Pardon Our Interruption", "_Incapsula_Resource" };

        /// <summary>Process-wide breaker shared by every Coles category scrape.</summary>
        public static ColesBlockBreaker Breaker { get; } = new(ColesBlockBreaker.CooldownFromEnvironment());

        /// <summary>
        /// Returns <paramref name="body"/> when it is a successful JSON response; otherwise throws.
        /// An Imperva block page also trips <paramref name="breaker"/>.
        /// </summary>
        public static string Validate(int status, string? contentType, string body, string url, ColesBlockBreaker? breaker = null)
        {
            breaker ??= Breaker;

            // Imperva answers with HTTP 200 and an HTML challenge page, so check the body before the status.
            if (IsImpervaBlock(body))
            {
                breaker.Trip();
                throw new ColesBlockedException($"Coles returned an Imperva bot-block page (HTTP {status}) for {url}");
            }

            if (status is < 200 or > 299)
            {
                throw new ColesScrapeException($"Coles returned HTTP {status} for {url}", status);
            }

            if (!LooksLikeJson(body))
            {
                throw new ColesScrapeException(
                    $"Coles returned a non-JSON response ({(string.IsNullOrWhiteSpace(contentType) ? "unknown" : contentType)}, {body.Length} bytes) for {url}");
            }

            return body;
        }

        public static ColesScrapeException ParseFailure(Exception ex) =>
            new($"Coles JSON: response could not be parsed: {ex.Message}", ex);

        /// <summary>A category that yields nothing is a failure, not an empty success.</summary>
        public static void EnsureProducts(int count, string url)
        {
            if (count <= 0)
            {
                throw new ColesScrapeException($"Coles JSON: extracted 0 products from {url}");
            }
        }

        /// <summary>Earlier pages were saved, but the scrape stopped before the last page.</summary>
        public static ColesScrapeException PartialFailure(int savedCount, Exception cause) =>
            new($"Coles JSON: saved {savedCount} products, then stopped early: {cause.Message}", cause);

        public static bool IsImpervaBlock(string? body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return false;
            }

            foreach (var marker in ImpervaMarkers)
            {
                if (body.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool LooksLikeJson(string body)
        {
            foreach (var c in body)
            {
                if (!char.IsWhiteSpace(c))
                {
                    return c == '{';
                }
            }

            return false;
        }
    }

    /// <summary>
    /// After Coles serves a bot-block page, keep every Coles category scrape away for a cooldown
    /// period, because each further request extends the block.
    /// </summary>
    public class ColesBlockBreaker
    {
        public const int DefaultCooldownMinutes = 180;

        private readonly TimeSpan _cooldown;
        private readonly Func<DateTime> _utcNow;
        private readonly object _gate = new();
        private DateTime? _blockedAtUtc;

        public ColesBlockBreaker(TimeSpan cooldown, Func<DateTime>? utcNow = null)
        {
            _cooldown = cooldown;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public DateTime? BlockedUntilUtc
        {
            get
            {
                lock (_gate)
                {
                    return _blockedAtUtc + _cooldown;
                }
            }
        }

        public void Trip()
        {
            if (_cooldown <= TimeSpan.Zero)
            {
                return;
            }

            lock (_gate)
            {
                _blockedAtUtc = _utcNow();
            }
        }

        public void ThrowIfOpen(string url)
        {
            DateTime blockedAt;
            lock (_gate)
            {
                if (_blockedAtUtc == null)
                {
                    return;
                }

                if (_utcNow() >= _blockedAtUtc.Value + _cooldown)
                {
                    _blockedAtUtc = null;
                    return;
                }

                blockedAt = _blockedAtUtc.Value;
            }

            throw new ColesBlockedException(
                $"Skipped {url}: Coles blocked us at {blockedAt:u}; cooling down until {blockedAt + _cooldown:u}");
        }

        public static TimeSpan CooldownFromEnvironment()
        {
            var raw = Environment.GetEnvironmentVariable("COLES_BLOCK_COOLDOWN_MINUTES");
            var minutes = int.TryParse(raw, out var v) && v >= 0 ? v : DefaultCooldownMinutes;
            return TimeSpan.FromMinutes(minutes);
        }
    }
}
