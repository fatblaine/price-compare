using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using PriceCompareCore.Exceptions;
using PriceCompareCore.Interfaces;

namespace PriceCompareCore.Services
{
    /// <summary>Settings for <see cref="ColesBrowserSession"/>; every value can be overridden by an environment variable.</summary>
    public sealed record ColesBrowserOptions
    {
        /// <summary>Playwright channel: "chrome" (installed Google Chrome), "msedge", or empty for the bundled Chromium.</summary>
        public string? Channel { get; init; } = "chrome";

        /// <summary>A visible window looks like a real visitor; headless Chrome is much more likely to be blocked.</summary>
        public bool Headless { get; init; }

        public int PageDelayMinMs { get; init; } = 2000;
        public int PageDelayMaxMs { get; init; } = 5000;
        public int NavigationDelayMinMs { get; init; } = 1000;
        public int NavigationDelayMaxMs { get; init; } = 3000;

        /// <summary>How long to wait for Imperva's JavaScript check to finish before calling it a block.</summary>
        public TimeSpan ChallengeTimeout { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>Close the browser after this long without a request; a weekly run keeps it busy every 10 minutes.</summary>
        public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(20);

        /// <summary>Where cookies are kept between restarts. Deleted whenever Coles blocks us.</summary>
        public string StorageStatePath { get; init; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PriceCompare", "coles-browser-state.json");

        public TimeSpan[] RetryDelays { get; init; } = { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45) };

        public static ColesBrowserOptions FromEnvironment()
        {
            var o = new ColesBrowserOptions();
            var channel = Environment.GetEnvironmentVariable("COLES_BROWSER_CHANNEL");
            var statePath = Environment.GetEnvironmentVariable("COLES_BROWSER_STATE_PATH");
            return o with
            {
                Channel = channel == null ? o.Channel : (string.IsNullOrWhiteSpace(channel) || channel == "bundled" ? null : channel.Trim()),
                Headless = bool.TryParse(Environment.GetEnvironmentVariable("COLES_BROWSER_HEADLESS"), out var h) ? h : o.Headless,
                PageDelayMinMs = Int("COLES_PAGE_DELAY_MIN_MS", o.PageDelayMinMs),
                PageDelayMaxMs = Int("COLES_PAGE_DELAY_MAX_MS", o.PageDelayMaxMs),
                NavigationDelayMinMs = Int("COLES_NAV_DELAY_MIN_MS", o.NavigationDelayMinMs),
                NavigationDelayMaxMs = Int("COLES_NAV_DELAY_MAX_MS", o.NavigationDelayMaxMs),
                ChallengeTimeout = TimeSpan.FromSeconds(Int("COLES_CHALLENGE_TIMEOUT_SECONDS", (int)o.ChallengeTimeout.TotalSeconds)),
                IdleTimeout = TimeSpan.FromMinutes(Int("COLES_BROWSER_IDLE_MINUTES", (int)o.IdleTimeout.TotalMinutes)),
                StorageStatePath = string.IsNullOrWhiteSpace(statePath) ? o.StorageStatePath : statePath.Trim()
            };
        }

        private static int Int(string name, int fallback) =>
            int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v >= 0 ? v : fallback;
    }

    /// <summary>
    /// Loads Coles category pages through one long-lived, real browser session, the way a visitor would:
    /// page 1 by opening <c>/browse/{slug}</c> (which also passes Imperva's JavaScript check and yields the
    /// current Next.js buildId), later pages by the same <c>_next/data</c> fetch the site makes when you
    /// click "next page". Requests are paced with random pauses. Nothing here hides automation, solves
    /// CAPTCHAs or rotates IPs: when Coles blocks us we stop (see <see cref="ColesBlockBreaker"/>).
    /// </summary>
    public sealed class ColesBrowserSession : IColesCategoryPageSource, IAsyncDisposable
    {
        private const string BaseUrl = "https://www.coles.com.au";
        private const string NextDataSelector = "script#__NEXT_DATA__";

        // Same request the Next.js router makes for client-side navigation.
        private const string FetchScript = @"async (url) => {
            const r = await fetch(url, { headers: { 'x-nextjs-data': '1' }, credentials: 'same-origin' });
            return JSON.stringify({ status: r.status, contentType: r.headers.get('content-type') || '', body: await r.text() });
        }";

        private readonly ILogger<ColesBrowserSession> _logger;
        private readonly ColesBrowserOptions _options;
        private readonly ColesBlockBreaker _breaker;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly Timer _idleTimer;

        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private IBrowserContext? _context;
        private IPage? _page;
        private string? _buildId;
        private DateTime _lastUsedUtc = DateTime.UtcNow;

        public ColesBrowserSession(ILogger<ColesBrowserSession> logger)
            : this(logger, ColesBrowserOptions.FromEnvironment(), ColesDomFetchGuard.Breaker)
        {
        }

        public ColesBrowserSession(ILogger<ColesBrowserSession> logger, ColesBrowserOptions options, ColesBlockBreaker breaker)
        {
            _logger = logger;
            _options = options;
            _breaker = breaker;
            _idleTimer = new Timer(_ => _ = CloseIfIdleAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public async Task<string> GetPageJsonAsync(ColesCategory category, int page, CancellationToken ct)
        {
            var url = page <= 1 ? $"{BaseUrl}/browse/{category.Slug}" : $"{BaseUrl}/browse/{category.Slug}?page={page}";

            for (var attempt = 1; ; attempt++)
            {
                _breaker.ThrowIfOpen(url);

                TimeSpan retryDelay;
                await _lock.WaitAsync(ct);
                try
                {
                    await EnsureStartedAsync();
                    return page <= 1
                        ? await OpenCategoryAsync(category, ct)
                        : await FetchDataPageAsync(category, page, ct);
                }
                catch (ColesBlockedException)
                {
                    // The cookies are now flagged; start clean after the cooldown.
                    await ResetAsync(deleteStorageState: true);
                    throw;
                }
                catch (ColesScrapeException ex) when (ex.IsTransient && attempt <= _options.RetryDelays.Length)
                {
                    retryDelay = _options.RetryDelays[attempt - 1];
                    _logger.LogWarning("Coles {Slug} page {Page}: {Message}; retry {Attempt} in {Delay}s",
                        category.Slug, page, ex.Message, attempt, retryDelay.TotalSeconds);
                }
                catch (Exception ex) when (IsBrowserFailure(ex) && attempt <= _options.RetryDelays.Length)
                {
                    retryDelay = _options.RetryDelays[attempt - 1];
                    _logger.LogWarning(ex, "Coles {Slug} page {Page}: browser error; restarting the browser, retry {Attempt} in {Delay}s",
                        category.Slug, page, attempt, retryDelay.TotalSeconds);
                    await ResetAsync(deleteStorageState: false);
                }
                catch (Exception ex) when (IsBrowserFailure(ex))
                {
                    throw new ColesScrapeException($"Browser error loading {url}: {ex.Message}", ex);
                }
                finally
                {
                    _lastUsedUtc = DateTime.UtcNow;
                    _lock.Release();
                }

                await Task.Delay(retryDelay, ct);
            }
        }

        /// <summary>Opens the category page like a visitor and returns page 1 from its embedded Next.js data.</summary>
        private async Task<string> OpenCategoryAsync(ColesCategory category, CancellationToken ct)
        {
            var url = $"{BaseUrl}/browse/{category.Slug}";
            await PauseAsync(_options.NavigationDelayMinMs, _options.NavigationDelayMaxMs, ct);

            _logger.LogInformation("Coles browser: opening {Url}", url);
            var response = await _page!.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000
            });

            var nextDataText = await WaitForNextDataAsync(url, ct);
            var nextData = JsonNode.Parse(nextDataText) ?? throw new ColesScrapeException($"Empty __NEXT_DATA__ on {url}");

            var nextPage = nextData["page"]?.GetValue<string>();
            if (nextPage == "/404")
            {
                throw new ColesScrapeException($"Coles has no category page at {url} (404); the slug may have changed", 404);
            }

            if (nextPage == "/_error")
            {
                var status = nextData["props"]?["pageProps"]?["statusCode"]?.GetValue<int>() ?? response?.Status ?? 500;
                throw new ColesScrapeException($"Coles returned an error page (HTTP {status}) for {url}", status);
            }

            _buildId = nextData["buildId"]?.GetValue<string>()
                ?? throw new ColesScrapeException($"No buildId in __NEXT_DATA__ on {url}");

            await SaveStorageStateAsync();

            var pageProps = nextData["props"]?["pageProps"];
            if (pageProps?["searchResults"] == null)
            {
                // Not embedded in the HTML for some reason: ask for page 1 the same way as later pages.
                return await FetchDataPageAsync(category, 1, ct);
            }

            return new JsonObject { ["pageProps"] = pageProps.DeepClone() }.ToJsonString();
        }

        /// <summary>Fetches a later page through the site's own Next.js data endpoint, from inside the page.</summary>
        private async Task<string> FetchDataPageAsync(ColesCategory category, int page, CancellationToken ct, bool buildIdRefreshed = false)
        {
            if (_buildId == null || !_page!.Url.StartsWith(BaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                // Fresh browser (e.g. after a restart): load the category first to get on-site and learn the buildId.
                await OpenCategoryAsync(category, ct);
            }

            var path = $"/_next/data/{_buildId}/en/browse/{category.Slug}.json?slug={category.Slug}&page={page}";
            await PauseAsync(_options.PageDelayMinMs, _options.PageDelayMaxMs, ct);

            var raw = await _page!.EvaluateAsync<string>(FetchScript, path);
            using var result = JsonDocument.Parse(raw);
            var status = result.RootElement.GetProperty("status").GetInt32();
            var contentType = result.RootElement.GetProperty("contentType").GetString();
            var body = result.RootElement.GetProperty("body").GetString() ?? string.Empty;

            if (status == 404 && page > 1 && !buildIdRefreshed && !ColesDomFetchGuard.IsImpervaBlock(body))
            {
                // Coles deployed since we loaded the page, so the buildId is stale. Reload once to pick up the new one.
                _logger.LogInformation("Coles browser: 404 for {Path}; refreshing buildId", path);
                await OpenCategoryAsync(category, ct);
                return await FetchDataPageAsync(category, page, ct, buildIdRefreshed: true);
            }

            if (ColesDomFetchGuard.IsImpervaBlock(body))
            {
                _logger.LogError("Coles browser: Imperva block page for {Path}; pausing Coles scrapes", path);
            }

            return ColesDomFetchGuard.Validate(status, contentType, body, BaseUrl + path, _breaker);
        }

        /// <summary>
        /// Waits until the Next.js data is in the page. Imperva's check runs first and may reload the page;
        /// if its block page is still showing when time runs out, that is a block.
        /// </summary>
        private async Task<string> WaitForNextDataAsync(string url, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + _options.ChallengeTimeout;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var handle = await _page!.QuerySelectorAsync(NextDataSelector);
                    var text = handle == null ? null : await handle.TextContentAsync();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
                catch (PlaywrightException)
                {
                    // The page navigated while we were looking (Imperva reloads after its check). Look again.
                }

                if (DateTime.UtcNow >= deadline)
                {
                    string html;
                    try
                    {
                        html = await _page!.ContentAsync();
                    }
                    catch (PlaywrightException)
                    {
                        html = string.Empty;
                    }

                    if (ColesDomFetchGuard.IsImpervaBlock(html))
                    {
                        _breaker.Trip();
                        _logger.LogError("Coles browser: Imperva block page on {Url}; pausing Coles scrapes until {Until:u}",
                            url, _breaker.BlockedUntilUtc);
                        throw new ColesBlockedException(
                            $"Coles showed its Imperva block page for {url} for more than {_options.ChallengeTimeout.TotalSeconds:0}s");
                    }

                    throw new ColesScrapeException(
                        $"Coles page {url} had no Next.js data after {_options.ChallengeTimeout.TotalSeconds:0}s", 408);
                }

                await Task.Delay(1000, ct);
            }
        }

        private async Task EnsureStartedAsync()
        {
            if (_page != null)
            {
                return;
            }

            _playwright = await Playwright.CreateAsync();
            _browser = await LaunchBrowserAsync(_playwright);

            var hasState = File.Exists(_options.StorageStatePath);
            _context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                Locale = "en-AU",
                TimezoneId = "Australia/Sydney",
                ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
                StorageStatePath = hasState ? _options.StorageStatePath : null
            });
            _page = await _context.NewPageAsync();
            _logger.LogInformation("Coles browser: started ({Channel}, headless={Headless}, saved cookies={HasState})",
                _options.Channel ?? "bundled chromium", _options.Headless, hasState);
        }

        private async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright)
        {
            var launch = new BrowserTypeLaunchOptions { Headless = _options.Headless, Channel = _options.Channel };
            try
            {
                return await playwright.Chromium.LaunchAsync(launch);
            }
            catch (PlaywrightException ex) when (_options.Channel != null)
            {
                _logger.LogWarning("Coles browser: channel '{Channel}' is not available ({Message}); using the bundled Chromium",
                    _options.Channel, ex.Message);
                launch.Channel = null;
                return await playwright.Chromium.LaunchAsync(launch);
            }
        }

        private async Task SaveStorageStateAsync()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_options.StorageStatePath)!);
                await _context!.StorageStateAsync(new BrowserContextStorageStateOptions { Path = _options.StorageStatePath });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlaywrightException)
            {
                _logger.LogWarning(ex, "Coles browser: could not save cookies to {Path}", _options.StorageStatePath);
            }
        }

        private static Task PauseAsync(int minMs, int maxMs, CancellationToken ct)
        {
            if (maxMs <= 0)
            {
                return Task.CompletedTask;
            }

            var low = Math.Min(minMs, maxMs);
            return Task.Delay(Random.Shared.Next(low, maxMs + 1), ct);
        }

        private static bool IsBrowserFailure(Exception ex) => ex is PlaywrightException or System.TimeoutException;

        private async Task CloseIfIdleAsync()
        {
            if (_page == null || DateTime.UtcNow - _lastUsedUtc < _options.IdleTimeout || !await _lock.WaitAsync(0))
            {
                return;
            }

            try
            {
                if (_page != null && DateTime.UtcNow - _lastUsedUtc >= _options.IdleTimeout)
                {
                    _logger.LogInformation("Coles browser: idle for {Minutes} min, closing", _options.IdleTimeout.TotalMinutes);
                    await ResetAsync(deleteStorageState: false);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task ResetAsync(bool deleteStorageState)
        {
            try
            {
                if (_context != null) await _context.CloseAsync();
                if (_browser != null) await _browser.CloseAsync();
            }
            catch (PlaywrightException ex)
            {
                _logger.LogDebug(ex, "Coles browser: error while closing");
            }
            finally
            {
                _playwright?.Dispose();
                _page = null;
                _context = null;
                _browser = null;
                _playwright = null;
                _buildId = null;
            }

            if (deleteStorageState && File.Exists(_options.StorageStatePath))
            {
                try
                {
                    File.Delete(_options.StorageStatePath);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Coles browser: could not delete {Path}", _options.StorageStatePath);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _idleTimer.DisposeAsync();
            await _lock.WaitAsync();
            try
            {
                await ResetAsync(deleteStorageState: false);
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
