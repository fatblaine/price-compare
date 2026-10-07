using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PriceCompareCore.Exceptions;

namespace PriceCompareCore.Services
{
    /// <summary>
    /// Shared fetch and validation for the Coles _next/data category scrapers (BTS-156).
    /// Anything that is not usable product JSON throws <see cref="ColesScrapeException"/>, so a
    /// blocked or broken scrape shows up as a failed job instead of "succeeded with 0 products".
    /// </summary>
    public static class ColesDomFetchGuard
    {
        private static readonly string[] ImpervaMarkers = { "Pardon Our Interruption", "_Incapsula_Resource" };

        /// <summary>Process-wide breaker shared by every Coles category scraper.</summary>
        public static ColesBlockBreaker Breaker { get; } = new(ColesBlockBreaker.CooldownFromEnvironment());

        public static async Task<string> FetchJsonAsync(
            HttpClient httpClient,
            string url,
            ILogger logger,
            CancellationToken ct,
            ColesBlockBreaker? breaker = null)
        {
            breaker ??= Breaker;
            breaker.ThrowIfOpen(url);

            HttpResponseMessage resp;
            try
            {
                resp = await httpClient.GetAsync(url, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                throw new ColesScrapeException($"Coles request timed out for {url}", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new ColesScrapeException($"Coles request failed for {url}: {ex.Message}", ex);
            }

            using (resp)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);

                // Imperva answers with HTTP 200 and an HTML challenge page, so check the body before the status.
                if (IsImpervaBlock(body))
                {
                    breaker.Trip();
                    logger.LogError("Coles JSON: Imperva bot-block page returned for {Url}; pausing Coles scrapes until {Until:u}",
                        url, breaker.BlockedUntilUtc);
                    throw new ColesBlockedException(
                        $"Coles returned an Imperva bot-block page (HTTP {(int)resp.StatusCode}) for {url}");
                }

                if (!resp.IsSuccessStatusCode)
                {
                    logger.LogWarning("Coles JSON: non-OK status {Status} for {Url}", (int)resp.StatusCode, url);
                    throw new ColesScrapeException($"Coles returned HTTP {(int)resp.StatusCode} for {url}");
                }

                if (!LooksLikeJson(body))
                {
                    var contentType = resp.Content.Headers.ContentType?.MediaType ?? "unknown";
                    logger.LogWarning("Coles JSON: non-JSON response ({ContentType}, {Length} bytes) for {Url}",
                        contentType, body.Length, url);
                    throw new ColesScrapeException(
                        $"Coles returned a non-JSON response ({contentType}, {body.Length} bytes) for {url}");
                }

                return body;
            }
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

        internal static bool IsImpervaBlock(string body)
        {
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
