namespace ProjectMD.Api.Services.Extraction;

// One request the browser wants to make, as the proxy sees it.
public sealed record ProxyRequest(
    string Method,
    Uri Url,
    IReadOnlyDictionary<string, string> Headers,
    byte[]? Body,
    string ResourceType,
    bool IsMainNavigation,
    int RedirectDepth = 0);

// What to do with the request.
public abstract record ProxyOutcome
{
    private ProxyOutcome()
    {
    }

    // Answer the browser with this response. Headers are ready to hand to the browser (no
    // hop-by-hop, encoding, length or cookie headers); UpstreamHeaders are the server's own
    // headers, for fingerprinting. FinalUrl is where the content really came from.
    public sealed record Fulfil(
        Uri FinalUrl,
        int Status,
        IReadOnlyList<KeyValuePair<string, string>> Headers,
        string? ContentType,
        byte[] Body,
        IReadOnlyList<KeyValuePair<string, string>> UpstreamHeaders) : ProxyOutcome;

    // The page itself redirected: load Target as a fresh top-level navigation instead. (A redirect
    // followed inside the browser would not be seen by us, so we never let it happen.)
    public sealed record Redirect(Uri Target) : ProxyOutcome;

    // Refuse the request. Error is set when the failure should become the scan's error if this was
    // the page itself (blocked address, not HTML, too large, unreachable, timed out...).
    public sealed record Abort(string Reason, ExtractionException? Error) : ProxyOutcome;
}

// Answers every request the browser makes, so the browser itself never touches the network (it is
// started with host name resolution switched off). For each request: skip what a scan does not
// need (media, live connections, and images and fonts unless a visual-asset budget was given, see
// Motion:LoadImagesAndFonts); check the address with the RequestGuard; fetch it
// through the GuardedHttpClient (which connects only to the checked public addresses); follow
// redirects one hop at a time, checking every hop (for the page itself, hand the redirect back so
// the browser re-navigates and we see it again); and enforce size limits (the page is cut at
// Scanner:MaxHtmlBytes; any other file over Scanner:MaxResourceBytes is refused; the whole page may
// load at most Scanner:MaxTotalBytes). One proxy is made per page fetch.
public sealed class PageRequestProxy
{
    // Kinds of request that are never needed to read a page.
    private static readonly HashSet<string> SkippedTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image", "media", "font", "texttrack", "eventsource", "websocket", "manifest" };

    private readonly GuardedHttpClient _client;
    private readonly RequestGuard _guard;
    private readonly ExtractionOptions _options;
    private int _requests;
    private long _totalBytes;
    private readonly ApiRecorder? _recorder;
    private readonly long _visualAssetBytes;
    private readonly int _visualAssetRequests;
    private long _visualBytes;
    private int _visualRequests;

    // Creates a proxy for one page fetch. The optional recorder is told about every answered
    // request (it only notes method, host, path template, status and JSON layout; see ApiRecorder).
    // It adds no network path: it only looks at what this proxy already fetched.
    // visualAssetBytes / visualAssetRequests: when above 0, images and fonts are fetched too (so
    // screenshots look right), within their own byte and request budget that is counted apart from
    // Scanner:MaxTotalBytes and Scanner:MaxRequestsPerPage. They pass the same address checks.
    public PageRequestProxy(
        GuardedHttpClient client,
        RequestGuard guard,
        ExtractionOptions options,
        ApiRecorder? recorder = null,
        long visualAssetBytes = 0,
        int visualAssetRequests = 0)
    {
        _client = client;
        _guard = guard;
        _options = options;
        _recorder = recorder;
        _visualAssetBytes = Math.Max(0, visualAssetBytes);
        _visualAssetRequests = Math.Max(0, visualAssetRequests);
    }

    // True for the kinds of request that only matter for how the page looks.
    private static bool IsVisualAsset(string resourceType) =>
        resourceType.Equals("image", StringComparison.OrdinalIgnoreCase) ||
        resourceType.Equals("font", StringComparison.OrdinalIgnoreCase);

    // True when images and fonts are being loaded for this page.
    private bool LoadsVisualAssets => _visualAssetBytes > 0 && _visualAssetRequests > 0;

    // Decides and performs one request. Only the caller's cancellation is thrown; everything else
    // becomes an outcome.
    public async Task<ProxyOutcome> HandleAsync(ProxyRequest request, CancellationToken ct)
    {
        var visual = IsVisualAsset(request.ResourceType) && LoadsVisualAssets;
        if (SkippedTypes.Contains(request.ResourceType) && !visual)
        {
            return new ProxyOutcome.Abort("skipped type " + request.ResourceType, null);
        }

        if (visual)
        {
            if (Interlocked.Increment(ref _visualRequests) > _visualAssetRequests)
            {
                return new ProxyOutcome.Abort("too many images or fonts", null);
            }
        }
        else if (Interlocked.Increment(ref _requests) > _options.MaxRequestsPerPage)
        {
            return new ProxyOutcome.Abort("too many requests", null);
        }

        var method = new HttpMethod(request.Method.ToUpperInvariant());
        var body = request.Body;
        var current = request.Url;

        for (var hops = request.RedirectDepth; ; hops++)
        {
            var decision = await _guard.CheckAsync(current.AbsoluteUri, hops, ct);
            if (!decision.Allowed)
            {
                return new ProxyOutcome.Abort(decision.Detail, FetchSupport.FromGuard(decision));
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
            try
            {
                using var message = BuildMessage(method, current, request.Headers, body);
                using var response = await _client.SendAsync(message, timeout.Token);
                var status = (int)response.StatusCode;

                if (IsRedirect(status) && response.Headers.Location is { } location)
                {
                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    if (next.Scheme is not ("http" or "https"))
                    {
                        return new ProxyOutcome.Abort("redirect to " + next.Scheme, new ExtractionException(ExtractionFailure.BlockedAddress, FetchSupport.BlockedMessage));
                    }

                    if (request.IsMainNavigation)
                    {
                        return new ProxyOutcome.Redirect(next);
                    }

                    (method, body) = AfterRedirect(status, method, body);
                    current = next;
                    continue;
                }

                var finished = await FinishAsync(request, current, response, status, visual, timeout.Token);

                // Images and fonts are not API calls; leaving them out keeps the API catalog (and
                // the page's cache fingerprint) exactly as it was before images were loaded.
                if (!visual)
                {
                    NoteForApiCapture(request, method, body, finished);
                }

                return finished;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                return new ProxyOutcome.Abort("timeout", new ExtractionException(ExtractionFailure.TimedOut, FetchSupport.TimedOutMessage, ex));
            }
            catch (Exception ex)
            {
                return new ProxyOutcome.Abort("send failed", MapSendError(ex));
            }
        }
    }

    // Tells the API recorder about an answered request: the page itself sets which site is being
    // scanned; every other answered request is a candidate API call or third-party request.
    private void NoteForApiCapture(ProxyRequest request, HttpMethod method, byte[]? body, ProxyOutcome outcome)
    {
        if (_recorder is null || outcome is not ProxyOutcome.Fulfil fulfil)
        {
            return;
        }

        if (request.IsMainNavigation)
        {
            _recorder.SetPage(fulfil.FinalUrl);
            return;
        }

        _recorder.Record(new ApiObservation(
            method.Method, fulfil.FinalUrl, request.ResourceType, fulfil.Status, fulfil.ContentType, body ?? [], fulfil.Body));
    }

    // Reads the body (within the limits) and builds the response for the browser. `visual` marks an
    // image or font, which is counted against the visual-asset budget instead of the main one.
    private async Task<ProxyOutcome> FinishAsync(ProxyRequest request, Uri url, HttpResponseMessage response, int status, bool visual, CancellationToken ct)
    {
        var upstream = GuardedHttpClient.AllHeaders(response);
        var declared = GuardedHttpClient.DeclaredLength(response);
        long cap;

        if (visual)
        {
            cap = Math.Min(_options.MaxResourceBytes, Math.Max(0, _visualAssetBytes - Interlocked.Read(ref _visualBytes)));
            if (declared > cap)
            {
                return new ProxyOutcome.Abort("image or font too large", null);
            }
        }
        else if (request.IsMainNavigation)
        {
            if (!FetchSupport.IsHtmlContentType(response.Content.Headers.ContentType?.ToString()))
            {
                return new ProxyOutcome.Abort("not html", new ExtractionException(ExtractionFailure.NotHtml, FetchSupport.NotHtmlMessage));
            }

            // A declared size far beyond the limit is refused; a bit over is cut below.
            if (declared > (long)_options.MaxHtmlBytes * 8)
            {
                return new ProxyOutcome.Abort("too large", new ExtractionException(ExtractionFailure.TooLarge, FetchSupport.TooLargeMessage));
            }

            cap = _options.MaxHtmlBytes;
        }
        else
        {
            cap = Math.Min(_options.MaxResourceBytes, Math.Max(0, _options.MaxTotalBytes - Interlocked.Read(ref _totalBytes)));
            if (declared > cap)
            {
                return new ProxyOutcome.Abort("resource too large", null);
            }
        }

        var (bytes, truncated) = await GuardedHttpClient.ReadBodyAsync(response, cap, ct);
        if (truncated && !request.IsMainNavigation)
        {
            return new ProxyOutcome.Abort("resource too large", null);
        }

        if (visual)
        {
            Interlocked.Add(ref _visualBytes, bytes.Length);
        }
        else
        {
            Interlocked.Add(ref _totalBytes, bytes.Length);
        }

        var headers = new List<KeyValuePair<string, string>>();
        foreach (var header in upstream)
        {
            if (!GuardedHttpClient.IsHopByHopOrManaged(header.Key) &&
                !header.Key.Equals("set-cookie", StringComparison.OrdinalIgnoreCase) &&
                !header.Key.Equals("content-type", StringComparison.OrdinalIgnoreCase))
            {
                headers.Add(header);
            }
        }

        return new ProxyOutcome.Fulfil(url, status, headers, response.Content.Headers.ContentType?.ToString(), bytes, upstream);
    }

    // Builds our request from the browser's: same method, body and headers, minus the ones the HTTP
    // stack manages itself (and cookies, which this client keeps in its own jar).
    private static HttpRequestMessage BuildMessage(HttpMethod method, Uri url, IReadOnlyDictionary<string, string> headers, byte[]? body)
    {
        var message = GuardedHttpClient.NewRequest(method, url, body);
        foreach (var (name, value) in headers)
        {
            if (GuardedHttpClient.IsHopByHopOrManaged(name) ||
                name.Equals("cookie", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(':'))
            {
                continue;
            }

            GuardedHttpClient.AddHeader(message, name, value);
        }

        return message;
    }

    // True for the status codes that redirect.
    private static bool IsRedirect(int status) => status is 301 or 302 or 303 or 307 or 308;

    // The method and body to use after a redirect: 303 (and 301/302 for POST) turn into a GET.
    private static (HttpMethod Method, byte[]? Body) AfterRedirect(int status, HttpMethod method, byte[]? body)
    {
        if (method == HttpMethod.Get || method == HttpMethod.Head)
        {
            return (method, null);
        }

        return status is 303 or 301 or 302 ? (HttpMethod.Get, null) : (method, body);
    }

    // Turns a failure while sending into a safe ExtractionException.
    private static ExtractionException MapSendError(Exception ex)
    {
        var blocked = GuardedHttpClient.FindBlocked(ex);
        if (blocked is not null)
        {
            return FetchSupport.FromGuard(blocked.Decision);
        }

        if (ex is HttpRequestException http)
        {
            return http.HttpRequestError switch
            {
                HttpRequestError.SecureConnectionError => new ExtractionException(ExtractionFailure.Unreachable, FetchSupport.CertificateMessage, ex),
                HttpRequestError.NameResolutionError => new ExtractionException(ExtractionFailure.Unreachable, FetchSupport.NotFoundMessage, ex),
                _ => new ExtractionException(ExtractionFailure.Unreachable, FetchSupport.UnreachableMessage, ex),
            };
        }

        return new ExtractionException(ExtractionFailure.Unreachable, FetchSupport.UnreachableMessage, ex);
    }
}
