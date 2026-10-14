using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace ProjectMD.Api.Services.Extraction;

// Loads a public page in a real (headless Chromium) browser so that JavaScript-rendered sites show
// their final content. One browser is shared by the whole app and started on first use; each fetch
// gets its own fresh browser context (no cookies, no storage, no cache shared between scans) that
// is thrown away afterwards. At most Scanner:MaxConcurrentFetches pages render at once.
//
// SSRF protection is the main job here. Playwright's own request interception is NOT enough on
// its own: the redirect hops of an intercepted request are followed inside the browser and never
// reach the interception handler (verified against Playwright 1.63 with a local server), so a
// public URL that redirects to http://169.254.169.254/ would slip through. So the design is:
//  1. IUrlValidator checks the URL text first.
//  2. The browser is started with host name resolution switched off (MAP * ~NOTFOUND), so it cannot
//     connect to anything by name by itself. It also gets no service workers, WebSockets are closed,
//     popups are closed and WebRTC is restricted.
//  3. context.RouteAsync("**/*") hands EVERY request (the page, scripts, styles, XHR...) to
//     PageRequestProxy, which checks the host with RequestGuard (public addresses only, http/https
//     only, safe ports only) and performs the request itself through GuardedHttpClient, which
//     connects only to the exact addresses that were checked (no DNS rebinding). Redirects are
//     followed one hop at a time with a check on each hop; when the page itself redirects, the
//     fetcher navigates again to the target so that this code sees it too.
// Images, fonts and media are never fetched. Downloads and dialogs are refused. The browser is
// never downloaded by this code: install it as described in backend/README.md. If it is missing,
// FetchAsync throws ExtractionException with the message "The page renderer is not installed on
// the server".
// TODO(robots): robots.txt is not read or respected yet.
public sealed class PlaywrightPageFetcher : IPageFetcher, IAsyncDisposable, IDisposable
{
    private const string UserAgent = "ProjectMD-Scanner/1.0";

    private readonly IUrlValidator _validator;
    private readonly Func<RequestGuard> _guardFactory;
    private readonly ExtractionOptions _options;
    private readonly ILogger<PlaywrightPageFetcher> _logger;
    private readonly SemaphoreSlim _slots;
    private readonly SemaphoreSlim _launchLock = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private bool _disposed;

    // Creates the fetcher. The browser is not started until the first fetch.
    public PlaywrightPageFetcher(
        IUrlValidator validator,
        IDnsResolver dns,
        IOptions<ExtractionOptions> options,
        ILogger<PlaywrightPageFetcher> logger)
        : this(validator, () => new RequestGuard(dns, options.Value.MaxRedirects), options, logger)
    {
    }

    // For tests: lets a test supply its own guard (for example one that treats loopback as public).
    internal PlaywrightPageFetcher(
        IUrlValidator validator,
        Func<RequestGuard> guardFactory,
        IOptions<ExtractionOptions> options,
        ILogger<PlaywrightPageFetcher> logger)
    {
        _validator = validator;
        _guardFactory = guardFactory;
        _options = options.Value;
        _logger = logger;
        _slots = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentFetches));
    }

    // What happened while one page loaded. Shared between the route handler and the fetch code.
    private sealed class FetchState
    {
        public readonly object Gate = new();
        public readonly List<string> Scripts = [];
        public readonly List<string> Styles = [];
        public ProxyOutcome.Fulfil? Main;
        public Uri? PendingNavigation;
        public ExtractionException? MainError;
        public int Redirects;
        public bool DownloadStarted;
    }

    // Fetches the page and returns what the browser rendered. Any expected failure is thrown as an
    // ExtractionException with a safe message; the caller's cancellation is passed through as is.
    public async Task<RawPage> FetchAsync(Uri url, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(url);

        var check = _validator.Validate(url.AbsoluteUri);
        if (!check.IsValid || check.Uri is null)
        {
            throw new ExtractionException(ExtractionFailure.BlockedAddress, check.Error ?? FetchSupport.BlockedMessage);
        }

        var target = check.Uri;

        var gotSlot = await _slots.WaitAsync(TimeSpan.FromSeconds(_options.QueueWaitSeconds), ct);
        if (!gotSlot)
        {
            throw new ExtractionException(ExtractionFailure.Busy, FetchSupport.BusyMessage);
        }

        try
        {
            using var overall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            overall.CancelAfter(TimeSpan.FromSeconds(_options.OverallTimeoutSeconds));
            return await FetchCoreAsync(target, overall.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // Not the caller: the overall time limit ran out.
            _logger.LogWarning("Page fetch for {Host} hit the overall time limit", UrlValidator.DisplayHost(target));
            throw new ExtractionException(ExtractionFailure.TimedOut, FetchSupport.TimedOutMessage, ex);
        }
        catch (Exception ex)
        {
            var mapped = FetchSupport.Map(ex);
            _logger.LogWarning(
                ex,
                "Page fetch for {Host} failed ({Failure})",
                UrlValidator.DisplayHost(target),
                mapped.Failure);
            throw mapped;
        }
        finally
        {
            _slots.Release();
        }
    }

    // Does the actual work with a fresh browser context and a fresh guarded HTTP client.
    private async Task<RawPage> FetchCoreAsync(Uri target, CancellationToken token)
    {
        var browser = await GetBrowserAsync(token);
        var state = new FetchState();
        var guard = _guardFactory();
        using var client = new GuardedHttpClient(guard);
        var apiRecorder = new ApiRecorder();
        var proxy = new PageRequestProxy(client, guard, _options, apiRecorder);
        var navigationMs = _options.NavigationTimeoutSeconds * 1000f;

        IBrowserContext? context = null;
        try
        {
            context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = UserAgent,
                JavaScriptEnabled = true,
                AcceptDownloads = false,
                ServiceWorkers = ServiceWorkerPolicy.Block,
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
                Locale = "en-US",
            }).WaitAsync(token);

            context.SetDefaultTimeout(navigationMs);
            context.SetDefaultNavigationTimeout(navigationMs);

            // Every request the page makes is answered by the proxy; none reach the network directly.
            await context.RouteAsync("**/*", route => HandleRouteAsync(route, proxy, state, token)).WaitAsync(token);

            // WebSockets are not needed to read a page: close them without connecting anywhere.
            await context.RouteWebSocketAsync("**/*", socket => Observe(socket.CloseAsync())).WaitAsync(token);

            var page = await context.NewPageAsync().WaitAsync(token);
            page.Dialog += (_, dialog) => Observe(dialog.DismissAsync());
            page.Download += (_, download) =>
            {
                state.DownloadStarted = true;
                Observe(download.CancelAsync());
            };

            // Close any popup the page opens.
            context.Page += (_, popup) =>
            {
                if (!ReferenceEquals(popup, page))
                {
                    Observe(popup.CloseAsync());
                }
            };

            await NavigateAsync(page, target, state, navigationMs, token);

            var main = state.Main ?? throw new ExtractionException(ExtractionFailure.Unreachable, FetchSupport.UnreachableMessage);

            // Read at most MaxHtmlBytes characters inside the browser so a huge DOM is never copied
            // across in full, then trim to the byte limit.
            var html = await page.EvaluateAsync<string>(
                "n => (document.documentElement ? document.documentElement.outerHTML : '').slice(0, n)",
                _options.MaxHtmlBytes).WaitAsync(token) ?? string.Empty;
            html = TextTools.TruncateUtf8(html, _options.MaxHtmlBytes);

            var finalUrl = Uri.TryCreate(page.Url, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https"
                ? parsed
                : main.FinalUrl;

            List<string> scripts;
            List<string> styles;
            lock (state.Gate)
            {
                scripts = [.. state.Scripts];
                styles = [.. state.Styles];
            }

            return new RawPage(finalUrl, main.Status, html, FetchSupport.FilterHeaders(main.UpstreamHeaders), scripts, styles, Api: apiRecorder.Build());
        }
        finally
        {
            if (context is not null)
            {
                try
                {
                    await context.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception)
                {
                    // Closing is best effort; a dead context is cleaned up with the browser.
                }
            }
        }
    }

    // Loads the page. When the page redirects, the proxy refuses the redirect and records where it
    // pointed; we then navigate there afresh (so the target is checked like any other request).
    private async Task NavigateAsync(IPage page, Uri start, FetchState state, float navigationMs, CancellationToken token)
    {
        var next = start;
        while (true)
        {
            state.PendingNavigation = null;
            try
            {
                await page.GotoAsync(next.AbsoluteUri, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = navigationMs,
                }).WaitAsync(token);
            }
            catch (System.TimeoutException) when (state.Main is not null && state.PendingNavigation is null)
            {
                // Some pages never go quiet (polling, chat widgets). The document did arrive, so
                // take what is on screen instead of failing.
            }
            catch (PlaywrightException) when (state.PendingNavigation is not null || state.MainError is not null || state.DownloadStarted)
            {
                // The proxy aborted the navigation on purpose; the reason is handled below.
            }

            if (state.Main is null && state.MainError is not null)
            {
                throw state.MainError;
            }

            if (state.DownloadStarted)
            {
                throw new ExtractionException(ExtractionFailure.NotHtml, FetchSupport.NotHtmlMessage);
            }

            if (state.PendingNavigation is not { } target)
            {
                return;
            }

            if (++state.Redirects > _options.MaxRedirects)
            {
                throw new ExtractionException(ExtractionFailure.Unreachable, FetchSupport.TooManyRedirectsMessage);
            }

            next = target;
        }
    }

    // Answers one request the page makes, through the proxy.
    private async Task HandleRouteAsync(IRoute route, PageRequestProxy proxy, FetchState state, CancellationToken token)
    {
        try
        {
            var request = route.Request;
            var isMain = IsMainNavigation(request);

            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var url))
            {
                await route.AbortAsync("blockedbyclient");
                return;
            }

            var outcome = await proxy.HandleAsync(
                new ProxyRequest(request.Method, url, request.Headers, request.PostDataBuffer, request.ResourceType, isMain, state.Redirects),
                token);

            switch (outcome)
            {
                case ProxyOutcome.Fulfil fulfil:
                    Record(state, request.ResourceType, request.Url);
                    if (isMain)
                    {
                        lock (state.Gate)
                        {
                            state.Main = fulfil;
                        }
                    }

                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        Status = fulfil.Status,
                        Headers = fulfil.Headers,
                        ContentType = fulfil.ContentType,
                        BodyBytes = fulfil.Body,
                    });
                    break;

                case ProxyOutcome.Redirect redirect:
                    lock (state.Gate)
                    {
                        state.PendingNavigation = redirect.Target;
                    }

                    await route.AbortAsync("aborted");
                    break;

                case ProxyOutcome.Abort abort:
                    if (isMain && abort.Error is not null)
                    {
                        lock (state.Gate)
                        {
                            state.MainError ??= abort.Error;
                        }
                    }

                    if (abort.Error is { Failure: ExtractionFailure.BlockedAddress })
                    {
                        _logger.LogWarning("Blocked a request: {Reason}", abort.Reason);
                    }

                    await route.AbortAsync("blockedbyclient");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            await TryAbortAsync(route);
        }
        catch (Exception ex)
        {
            // Fail closed: if anything goes wrong deciding, the request does not happen.
            _logger.LogDebug(ex, "A page request failed");
            await TryAbortAsync(route);
        }
    }

    // Aborts a request, ignoring the case where the page is already gone.
    private static async Task TryAbortAsync(IRoute route)
    {
        try
        {
            await route.AbortAsync("failed");
        }
        catch (Exception)
        {
            // The page or context is already gone.
        }
    }

    // Remembers the address of a script or stylesheet (not its content).
    private static void Record(FetchState state, string type, string url)
    {
        var list = type switch
        {
            "script" => state.Scripts,
            "stylesheet" => state.Styles,
            _ => null,
        };

        if (list is null)
        {
            return;
        }

        var clean = url.Split('#')[0];
        lock (state.Gate)
        {
            if (list.Count < 200 && clean.Length <= 500 && !list.Contains(clean))
            {
                list.Add(clean);
            }
        }
    }

    // True for the request that loads the page itself, not for frames inside it.
    private static bool IsMainNavigation(IRequest request)
    {
        try
        {
            return request.IsNavigationRequest && request.Frame.ParentFrame is null;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    // Starts the shared browser on first use and again if it has crashed.
    private async Task<IBrowser> GetBrowserAsync(CancellationToken token)
    {
        var current = _browser;
        if (current is { IsConnected: true })
        {
            return current;
        }

        await _launchLock.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_browser is { IsConnected: true })
            {
                return _browser;
            }

            if (_browser is not null)
            {
                try
                {
                    await _browser.CloseAsync();
                }
                catch (Exception)
                {
                    // Already dead.
                }

                _browser = null;
            }

            try
            {
                _playwright ??= await Playwright.CreateAsync();
                var channel = _options.BrowserChannel.Trim();
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    Channel = channel.Length == 0 ? null : channel,
                    Timeout = 30_000,
                    Args =
                    [
                        // The browser must never resolve a name itself: all fetching is done by the
                        // guarded HTTP client behind the route handler.
                        "--host-resolver-rules=MAP * ~NOTFOUND",
                        "--disable-dev-shm-usage",
                        "--mute-audio",
                        "--force-webrtc-ip-handling-policy=disable_non_proxied_udp",
                    ],
                });
                return _browser;
            }
            catch (Exception ex) when (ex is PlaywrightException or DirectoryNotFoundException or FileNotFoundException)
            {
                var message = FetchSupport.IsMissingBrowser(ex)
                    ? FetchSupport.RendererMissingMessage
                    : FetchSupport.RendererFailedMessage;
                throw new ExtractionException(ExtractionFailure.RendererMissing, message, ex);
            }
        }
        finally
        {
            _launchLock.Release();
        }
    }

    // Stops watching a fire-and-forget task but makes sure its failure is not left unobserved.
    private static void Observe(Task task) =>
        _ = task.ContinueWith(
            t => { _ = t.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    // Synchronous disposal (some hosts and tests dispose the container synchronously): does the
    // same work as DisposeAsync and waits for it.
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    // Closes the browser and the Playwright driver when the app shuts down.
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        try
        {
            if (_browser is not null)
            {
                await _browser.CloseAsync();
            }
        }
        catch (Exception)
        {
            // Shutting down anyway.
        }

        _playwright?.Dispose();
        _browser = null;
        _playwright = null;
    }
}
