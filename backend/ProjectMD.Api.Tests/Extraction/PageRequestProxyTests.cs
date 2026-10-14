using System.Text;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Tests.Extraction;

// Tests for the guarded HTTP client and the request proxy against a local test server. The names
// "site.test", "other.test" and "internal.test" are made up and resolved by a fake DNS; the guard
// used here treats loopback as public (so the local server can play a website) but still refuses
// private and link-local ranges. No external network is used.
public class PageRequestProxyTests
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders = new Dictionary<string, string>();

    // A proxy wired to a fake DNS and the test guard, with optional option changes.
    private static (PageRequestProxy Proxy, GuardedHttpClient Client, ExtractionTestSupport.FakeDns Dns) Build(Action<ExtractionOptions>? tweak = null)
    {
        var dns = LocalSite.Dns();
        var guard = LocalSite.Guard(dns);
        var options = new ExtractionOptions { RequestTimeoutSeconds = 5 };
        tweak?.Invoke(options);
        var client = new GuardedHttpClient(guard);
        return (new PageRequestProxy(client, guard, options), client, dns);
    }

    // A browser-style request for a URL.
    private static ProxyRequest Get(string url, string type = "script", bool main = false, string method = "GET", byte[]? body = null) =>
        new(method, new Uri(url), NoHeaders, body, type, main);

    // A plain successful request is answered with the body, status and content type; the browser
    // gets no cookie, length, encoding or hop-by-hop headers.
    [Fact]
    public async Task Plain_request_is_fulfilled_through_the_pinned_connection()
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, dns) = Build();
        using var _ = client;

        var outcome = await proxy.HandleAsync(Get(site.Url("/site.css"), "stylesheet"), CancellationToken.None);

        var fulfil = Assert.IsType<ProxyOutcome.Fulfil>(outcome);
        Assert.Equal(200, fulfil.Status);
        Assert.Equal("body { color: black; }", Encoding.UTF8.GetString(fulfil.Body));
        Assert.StartsWith("text/css", fulfil.ContentType);
        Assert.DoesNotContain(fulfil.Headers, h => h.Key.Equals("content-length", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fulfil.Headers, h => h.Key.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fulfil.Headers, h => h.Key.Equals("content-type", StringComparison.OrdinalIgnoreCase));

        // The server saw the made-up host name in the Host header, and DNS was asked exactly once.
        Assert.Equal(["site.test /site.css"], site.Hits);
        Assert.Equal(1, dns.Calls["site.test"]);
    }

    // Many requests to the same host resolve the name once, so every connection uses the same
    // checked addresses (a second, different DNS answer is never used).
    [Fact]
    public async Task Dns_is_asked_once_per_host_however_many_connections_are_made()
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, dns) = Build();
        using var _ = client;

        for (var i = 0; i < 5; i++)
        {
            await proxy.HandleAsync(Get(site.Url("/site.css?n=" + i), "stylesheet"), CancellationToken.None);
        }

        Assert.Equal(5, site.Hits.Count);
        Assert.Equal(1, dns.Calls["site.test"]);
    }

    // Gzip bodies are decoded, and the encoding headers are not passed on.
    [Fact]
    public async Task Compressed_bodies_are_decoded()
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, _) = Build();
        using var _c = client;

        var outcome = await proxy.HandleAsync(Get(site.Url("/gzip"), "document", main: true), CancellationToken.None);

        var fulfil = Assert.IsType<ProxyOutcome.Fulfil>(outcome);
        Assert.Contains("Compressed page body text", Encoding.UTF8.GetString(fulfil.Body));
        Assert.DoesNotContain(fulfil.Headers, h => h.Key.Equals("content-encoding", StringComparison.OrdinalIgnoreCase));
    }

    // A sub-resource redirect is followed inside the proxy (hop by hop) and the final content is returned.
    [Fact]
    public async Task Subresource_redirects_are_followed_hop_by_hop()
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, _) = Build();
        using var _c = client;

        var outcome = await proxy.HandleAsync(Get(site.Url("/s1.js")), CancellationToken.None);

        var fulfil = Assert.IsType<ProxyOutcome.Fulfil>(outcome);
        Assert.Contains("Rendered by JavaScript", Encoding.UTF8.GetString(fulfil.Body));
        Assert.Equal(["/s1.js", "/s2.js"], site.Paths);
        Assert.EndsWith("/s2.js", fulfil.FinalUrl.AbsoluteUri);
    }

    // A redirect of the page itself is NOT followed: it is handed back so the browser navigates
    // again (and the new address is checked like any other).
    [Fact]
    public async Task Main_navigation_redirect_is_handed_back_not_followed()
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, _) = Build();
        using var _c = client;

        var outcome = await proxy.HandleAsync(Get(site.Url("/r1"), "document", main: true), CancellationToken.None);

        var redirect = Assert.IsType<ProxyOutcome.Redirect>(outcome);
        Assert.Equal(site.Url("/r2"), redirect.Target.AbsoluteUri.TrimEnd('/'));
        Assert.Equal(["/r1"], site.Paths);
    }

    // A redirect to a host that resolves to a private address is refused, and the private host is
    // never contacted (the local server records nothing for it).
    [Fact]
    public async Task Redirect_to_a_private_host_is_blocked()
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, dns) = Build();
        using var _c = client;

        var outcome = await proxy.HandleAsync(Get(site.Url("/to-internal")), CancellationToken.None);

        var abort = Assert.IsType<ProxyOutcome.Abort>(outcome);
        Assert.Equal(ExtractionFailure.BlockedAddress, abort.Error!.Failure);
        Assert.Equal(["/to-internal"], site.Paths);
        Assert.Equal(1, dns.Calls["internal.test"]);
    }

    // Redirects to the cloud metadata address and to non-http schemes are refused.
    [Theory]
    [InlineData("/to-metadata")]
    [InlineData("/to-file")]
    public async Task Redirect_to_metadata_address_or_file_scheme_is_blocked(string path)
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, _) = Build();
        using var _c = client;

        var outcome = await proxy.HandleAsync(Get(site.Url(path)), CancellationToken.None);

        var abort = Assert.IsType<ProxyOutcome.Abort>(outcome);
        Assert.Equal(ExtractionFailure.BlockedAddress, abort.Error!.Failure);
    }

    // The same redirect-to-private protection for the page itself: the redirect is handed back, and
    // the follow-up navigation to the private host is refused by the guard.
    [Fact]
    public async Task Navigating_to_a_private_host_is_blocked_before_any_connection()
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, _) = Build();
        using var _c = client;

        var outcome = await proxy.HandleAsync(Get(site.Url("/secret", host: "internal.test"), "document", main: true), CancellationToken.None);

        var abort = Assert.IsType<ProxyOutcome.Abort>(outcome);
        Assert.Equal(ExtractionFailure.BlockedAddress, abort.Error!.Failure);
        Assert.Empty(site.Hits);
    }

    // A redirect loop is cut off.
    [Fact]
    public async Task Redirect_loops_are_capped()
    {
        await using var site = await LocalSite.StartAsync();
        var dns = LocalSite.Dns();
        var guard = LocalSite.Guard(dns, maxRedirects: 3);
        using var client = new GuardedHttpClient(guard);
        var proxy = new PageRequestProxy(client, guard, new ExtractionOptions { RequestTimeoutSeconds = 5 });

        var outcome = await proxy.HandleAsync(Get(site.Url("/loop")), CancellationToken.None);

        var abort = Assert.IsType<ProxyOutcome.Abort>(outcome);
        Assert.Equal(ExtractionFailure.Unreachable, abort.Error!.Failure);
        Assert.Equal(FetchSupport.TooManyRedirectsMessage, abort.Error.Message);
        Assert.Equal(4, site.Hits.Count);
    }

    // Cookies set along the way are sent on the follow-up request (some sites redirect until a
    // cookie arrives), but are not passed on to the browser.
    [Fact]
    public async Task Cookies_are_kept_for_follow_up_requests_but_not_given_to_the_browser()
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, _) = Build();
        using var _c = client;

        var outcome = await proxy.HandleAsync(Get(site.Url("/cookie-set")), CancellationToken.None);

        var fulfil = Assert.IsType<ProxyOutcome.Fulfil>(outcome);
        Assert.Equal(200, fulfil.Status);
        Assert.DoesNotContain(fulfil.Headers, h => h.Key.Equals("set-cookie", StringComparison.OrdinalIgnoreCase));
    }

    // Images, fonts, media and live connections are never fetched.
    [Theory]
    [InlineData("image")]
    [InlineData("font")]
    [InlineData("media")]
    [InlineData("eventsource")]
    [InlineData("websocket")]
    public async Task Unneeded_resource_types_are_skipped(string type)
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, _) = Build();
        using var _c = client;

        var outcome = await proxy.HandleAsync(Get(site.Url("/site.css"), type), CancellationToken.None);

        Assert.IsType<ProxyOutcome.Abort>(outcome);
        Assert.Empty(site.Hits);
    }

    // The page itself is cut at MaxHtmlBytes instead of failing.
    [Fact]
    public async Task The_page_is_truncated_at_the_html_limit()
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, _) = Build(o => o.MaxHtmlBytes = 20_000);
        using var _c = client;

        var outcome = await proxy.HandleAsync(Get(site.Url("/big"), "document", main: true), CancellationToken.None);

        var fulfil = Assert.IsType<ProxyOutcome.Fulfil>(outcome);
        Assert.Equal(20_000, fulfil.Body.Length);
    }

    // Any other file over the per-file limit is refused, not cut.
    [Fact]
    public async Task Oversized_subresources_are_refused()
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, _) = Build(o => o.MaxResourceBytes = 100_000);
        using var _c = client;

        var outcome = await proxy.HandleAsync(Get(site.Url("/bigscript.js")), CancellationToken.None);

        Assert.IsType<ProxyOutcome.Abort>(outcome);
    }

    // The whole page may load only so many bytes; later files are refused.
    [Fact]
    public async Task The_total_byte_budget_is_enforced()
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, _) = Build(o =>
        {
            o.MaxResourceBytes = 250_000;
            o.MaxTotalBytes = 300_000;
        });
        using var _c = client;

        var first = await proxy.HandleAsync(Get(site.Url("/bigscript.js?1")), CancellationToken.None);
        var second = await proxy.HandleAsync(Get(site.Url("/bigscript.js?2")), CancellationToken.None);

        Assert.IsType<ProxyOutcome.Fulfil>(first);
        Assert.IsType<ProxyOutcome.Abort>(second);
    }

    // The page itself must be HTML.
    [Fact]
    public async Task A_page_that_is_not_html_is_refused()
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, _) = Build();
        using var _c = client;

        var outcome = await proxy.HandleAsync(Get(site.Url("/json"), "document", main: true), CancellationToken.None);

        var abort = Assert.IsType<ProxyOutcome.Abort>(outcome);
        Assert.Equal(ExtractionFailure.NotHtml, abort.Error!.Failure);
    }

    // The same JSON is fine as a data request made by the page (xhr/fetch).
    [Fact]
    public async Task Json_is_fine_as_a_data_request()
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, _) = Build();
        using var _c = client;

        var outcome = await proxy.HandleAsync(Get(site.Url("/json"), "fetch"), CancellationToken.None);

        Assert.Contains("world", Encoding.UTF8.GetString(Assert.IsType<ProxyOutcome.Fulfil>(outcome).Body));
    }

    // Slow responses time out.
    [Fact]
    public async Task Slow_responses_time_out()
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, _) = Build(o => o.RequestTimeoutSeconds = 1);
        using var _c = client;

        var outcome = await proxy.HandleAsync(Get(site.Url("/slow"), "document", main: true), CancellationToken.None);

        var abort = Assert.IsType<ProxyOutcome.Abort>(outcome);
        Assert.Equal(ExtractionFailure.TimedOut, abort.Error!.Failure);
    }

    // The caller's cancellation is passed through, not turned into an outcome.
    [Fact]
    public async Task Caller_cancellation_is_passed_through()
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, _) = Build();
        using var _c = client;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => proxy.HandleAsync(Get(site.Url("/slow")), cts.Token));
    }

    // POST bodies are sent along.
    [Fact]
    public async Task Post_bodies_are_forwarded()
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, _) = Build();
        using var _c = client;
        var request = new ProxyRequest("POST", new Uri(site.Url("/echo")), new Dictionary<string, string> { ["content-type"] = "text/plain" }, Encoding.UTF8.GetBytes("ping"), "fetch", false);

        var outcome = await proxy.HandleAsync(request, CancellationToken.None);

        Assert.Equal("ping", Encoding.UTF8.GetString(Assert.IsType<ProxyOutcome.Fulfil>(outcome).Body));
    }

    // Too many requests from one page are cut off.
    [Fact]
    public async Task The_request_count_is_capped()
    {
        await using var site = await LocalSite.StartAsync();
        var (proxy, client, _) = Build(o => o.MaxRequestsPerPage = 2);
        using var _c = client;

        var results = new List<ProxyOutcome>();
        for (var i = 0; i < 4; i++)
        {
            results.Add(await proxy.HandleAsync(Get(site.Url("/site.css?" + i), "stylesheet"), CancellationToken.None));
        }

        Assert.Equal(2, results.Count(r => r is ProxyOutcome.Fulfil));
        Assert.Equal(2, results.Count(r => r is ProxyOutcome.Abort));
    }

    // Ports that belong to other kinds of services (mail, databases...) are refused.
    [Theory]
    [InlineData("http://site.test:25/")]
    [InlineData("http://site.test:6379/")]
    [InlineData("http://site.test:22/")]
    [InlineData("http://site.test:3306/")]
    public async Task Restricted_ports_are_refused_without_a_connection(string url)
    {
        var (proxy, client, dns) = Build();
        using var _c = client;

        var outcome = await proxy.HandleAsync(Get(url, "document", main: true), CancellationToken.None);

        var abort = Assert.IsType<ProxyOutcome.Abort>(outcome);
        Assert.Equal(ExtractionFailure.BlockedAddress, abort.Error!.Failure);
        Assert.Empty(dns.Calls);
    }

    // A connection failure is reported with the safe message.
    [Fact]
    public async Task Connection_failures_use_a_safe_message()
    {
        var dns = LocalSite.Dns();
        var guard = LocalSite.Guard(dns);
        using var client = new GuardedHttpClient(guard);
        var proxy = new PageRequestProxy(client, guard, new ExtractionOptions { RequestTimeoutSeconds = 5 });

        // Nothing listens on this port (49 is below the restricted list's reach and not in use).
        var outcome = await proxy.HandleAsync(Get("http://site.test:49/", "document", main: true), CancellationToken.None);

        var abort = Assert.IsType<ProxyOutcome.Abort>(outcome);
        Assert.Equal(ExtractionFailure.Unreachable, abort.Error!.Failure);
        Assert.Equal(FetchSupport.UnreachableMessage, abort.Error.Message);
    }
}
