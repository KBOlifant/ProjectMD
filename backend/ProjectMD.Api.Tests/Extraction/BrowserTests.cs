using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectMD.Api.Services;
using ProjectMD.Api.Services.Extraction;
using static ProjectMD.Api.Tests.Extraction.ExtractionTestSupport;

namespace ProjectMD.Api.Tests.Extraction;

// A [Fact] that is skipped unless the environment variable PROJECTMD_BROWSER_TESTS is "1". The
// tests using it need a Playwright browser (see backend/README.md); optionally set
// PROJECTMD_BROWSER_CHANNEL to "chrome" or "msedge" to use an installed browser instead.
public sealed class BrowserFactAttribute : FactAttribute
{
    public BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PROJECTMD_BROWSER_TESTS") != "1")
        {
            Skip = "Set PROJECTMD_BROWSER_TESTS=1 (and install a browser) to run browser tests.";
        }
    }
}

// Tests that start browser-related code. They share one collection so they never run in parallel
// (one of them changes an environment variable for the whole process).
[Collection("Playwright")]
public class BrowserTests
{
    // The browser channel to use in the opt-in tests (empty = Playwright's own Chromium).
    private static string Channel => Environment.GetEnvironmentVariable("PROJECTMD_BROWSER_CHANNEL") ?? string.Empty;

    // With no browser installed the fetcher says so with the documented message. Always runs: it
    // points Playwright at an empty folder, so no real browser is found and nothing is fetched.
    [Fact]
    public async Task Missing_browser_gives_the_documented_message()
    {
        var empty = Path.Combine(Path.GetTempPath(), "projectmd-no-browsers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        var previous = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", empty);
        try
        {
            await using var fetcher = new PlaywrightPageFetcher(
                new UrlValidator(), new FakeDns(), Options.Create(new ExtractionOptions()), NullLogger<PlaywrightPageFetcher>.Instance);

            var ex = await Assert.ThrowsAsync<ExtractionException>(() => fetcher.FetchAsync(new Uri("https://example.com/"), CancellationToken.None));

            Assert.Equal(ExtractionFailure.RendererMissing, ex.Failure);
            Assert.Equal("The page renderer is not installed on the server", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", previous);
            try
            {
                Directory.Delete(empty, recursive: true);
            }
            catch (IOException)
            {
                // Temp folder cleanup is best effort.
            }
        }
    }

    // Smoke test of the whole pipeline against the one public page allowed for it.
    [BrowserFact]
    public async Task Fetches_and_reduces_example_com()
    {
        var options = new ExtractionOptions { BrowserChannel = Channel };
        await using var fetcher = new PlaywrightPageFetcher(
            new UrlValidator(), new SystemDnsResolver(), Options.Create(options), NullLogger<PlaywrightPageFetcher>.Instance);
        var service = new ExtractionService(fetcher, new HtmlReducer(), Options.Create(options));

        var digest = await service.ExtractAsync(new Uri("https://example.com/"), CancellationToken.None);

        // The wording of example.com changes now and then, so only stable facts are checked.
        Assert.Equal(200, digest.StatusCode);
        Assert.Equal("Example Domain", digest.Title);
        Assert.True(digest.Text.Length > 20);
        Assert.NotEmpty(digest.Links);
        Assert.Matches("^[0-9a-f]{64}$", digest.ContentHash);
        Assert.Contains("content-type", digest.Headers.Keys.Select(k => k.ToLowerInvariant()));
        Assert.StartsWith("https://example.com", digest.FinalUrl.AbsoluteUri);
        Assert.True(digest.ReducedChars < digest.RawHtmlChars);
    }

    // A fetcher that talks to the local test site: made-up host names resolved by a fake DNS and a
    // guard that accepts loopback; everything else about the guard is the real thing.
    private static PlaywrightPageFetcher LocalFetcher(Action<ExtractionOptions>? tweak = null)
    {
        var options = new ExtractionOptions { BrowserChannel = Channel, RequestTimeoutSeconds = 5, NavigationTimeoutSeconds = 15, OverallTimeoutSeconds = 40 };
        tweak?.Invoke(options);
        var dns = LocalSite.Dns();
        return new PlaywrightPageFetcher(
            new UrlValidator(), () => LocalSite.Guard(dns, options.MaxRedirects), Options.Create(options), NullLogger<PlaywrightPageFetcher>.Instance);
    }

    // The page is rendered with JavaScript; the page redirect chain and the script redirect are all
    // followed through the guard, and the final address and the asset addresses are reported.
    [BrowserFact]
    public async Task Renders_javascript_and_follows_redirects_through_the_guard()
    {
        await using var site = await LocalSite.StartAsync();
        await using var fetcher = LocalFetcher();

        var page = await fetcher.FetchAsync(new Uri(site.Url("/r1")), CancellationToken.None);

        Assert.Equal(200, page.StatusCode);
        Assert.Equal(new Uri(site.Url("/final")), page.FinalUrl);
        Assert.Contains("Rendered by JavaScript after a redirect", page.Html);
        Assert.Contains(site.Url("/s1.js"), page.ScriptUrls);
        Assert.Contains(site.Url("/site.css"), page.StylesheetUrls);
        Assert.Contains("content-type", page.Headers.Keys);
        Assert.Equal(["/r1", "/r2", "/final"], site.Paths.Take(3));
        Assert.Contains("/s2.js", site.Paths);
        Assert.Contains("/site.css", site.Paths);
    }

    // A page that redirects to a private host, to the cloud metadata address or to a file is refused.
    [BrowserFact]
    public async Task Page_redirects_to_private_targets_are_refused()
    {
        await using var site = await LocalSite.StartAsync();
        await using var fetcher = LocalFetcher();

        foreach (var path in new[] { "/to-internal", "/to-metadata", "/to-file" })
        {
            var ex = await Assert.ThrowsAsync<ExtractionException>(() => fetcher.FetchAsync(new Uri(site.Url(path)), CancellationToken.None));

            Assert.Equal(ExtractionFailure.BlockedAddress, ex.Failure);
            Assert.True(ex.IsClientError);
            Assert.DoesNotContain("169.254", ex.Message);
            Assert.DoesNotContain("internal.test", ex.Message);
        }
    }

    // A page whose own address resolves to a private IP is refused before anything is sent.
    [BrowserFact]
    public async Task Page_on_a_private_address_is_refused_without_contacting_it()
    {
        await using var site = await LocalSite.StartAsync();
        await using var fetcher = LocalFetcher();

        var ex = await Assert.ThrowsAsync<ExtractionException>(() => fetcher.FetchAsync(new Uri(site.Url("/", host: "internal.test")), CancellationToken.None));

        Assert.Equal(ExtractionFailure.BlockedAddress, ex.Failure);
        Assert.Empty(site.Hits);
    }

    // A script on the page that redirects to the cloud metadata address is blocked; the page itself
    // still loads. The script's own request is refused, so its code never runs.
    [BrowserFact]
    public async Task A_sub_resource_redirecting_to_an_internal_address_is_blocked_but_the_page_loads()
    {
        await using var site = await LocalSite.StartAsync();
        await using var fetcher = LocalFetcher();

        var page = await fetcher.FetchAsync(new Uri(site.Url("/with-internal-script")), CancellationToken.None);

        Assert.Equal(200, page.StatusCode);
        Assert.Contains("before", page.Html);
    }

    // Requests made by the page's own JavaScript are guarded too: private targets fail, public
    // ones (the made-up other.test) go through the proxy.
    [BrowserFact]
    public async Task Requests_from_page_scripts_are_guarded()
    {
        await using var site = await LocalSite.StartAsync(app =>
        {
            app.MapGet("/probe", (Microsoft.AspNetCore.Http.HttpContext c) => Microsoft.AspNetCore.Http.Results.Content(
                "<html><body><main><p>Probe page with enough text to be a real page.</p><ul id=\"out\"></ul></main><script>" +
                "function log(t){var li=document.createElement('li');li.textContent=t;document.getElementById('out').appendChild(li);}" +
                "var port='" + c.Request.Host.Port + "';" +
                "Promise.allSettled([" +
                "fetch('http://10.20.30.40:8080/').then(()=>log('private-ip:reached'),()=>log('private-ip:blocked'))," +
                "fetch('http://internal.test:'+port+'/secret').then(()=>log('internal-name:reached'),()=>log('internal-name:blocked'))," +
                "fetch('http://169.254.169.254/latest/meta-data/').then(()=>log('metadata:reached'),()=>log('metadata:blocked'))," +
                "fetch('http://other.test:'+port+'/json').then(()=>log('public-name:reached'),()=>log('public-name:blocked'))" +
                "]);" +
                "</script></body></html>", "text/html"));
        });
        await using var fetcher = LocalFetcher();

        var page = await fetcher.FetchAsync(new Uri(site.Url("/probe")), CancellationToken.None);

        // The script's own source also contains these words, so look at the list items it created.
        Assert.Contains("<li>private-ip:blocked</li>", page.Html);
        Assert.Contains("<li>internal-name:blocked</li>", page.Html);
        Assert.Contains("<li>metadata:blocked</li>", page.Html);
        Assert.Contains("<li>public-name:reached</li>", page.Html);
        Assert.DoesNotContain("<li>private-ip:reached</li>", page.Html);
        Assert.DoesNotContain("<li>internal-name:reached</li>", page.Html);
        Assert.DoesNotContain("<li>metadata:reached</li>", page.Html);
        Assert.DoesNotContain(site.Hits, h => h.StartsWith("internal.test"));
    }

    // Pages that are not HTML, too slow, or loop forever are refused with the right failure.
    [BrowserFact]
    public async Task Bad_pages_fail_with_safe_errors()
    {
        await using var site = await LocalSite.StartAsync();
        await using var fetcher = LocalFetcher(o =>
        {
            o.RequestTimeoutSeconds = 2;
            o.MaxRedirects = 3;
        });

        var notHtml = await Assert.ThrowsAsync<ExtractionException>(() => fetcher.FetchAsync(new Uri(site.Url("/json")), CancellationToken.None));
        Assert.Equal(ExtractionFailure.NotHtml, notHtml.Failure);

        var slow = await Assert.ThrowsAsync<ExtractionException>(() => fetcher.FetchAsync(new Uri(site.Url("/slow")), CancellationToken.None));
        Assert.Equal(ExtractionFailure.TimedOut, slow.Failure);

        var loop = await Assert.ThrowsAsync<ExtractionException>(() => fetcher.FetchAsync(new Uri(site.Url("/loop")), CancellationToken.None));
        Assert.Equal(ExtractionFailure.Unreachable, loop.Failure);
        Assert.Equal(FetchSupport.TooManyRedirectsMessage, loop.Message);
    }

    // HTTP error pages are returned with their status (the extraction service decides what to do);
    // the page is cut at the HTML limit instead of failing; cookies set during redirects work.
    [BrowserFact]
    public async Task Status_size_limit_and_cookies()
    {
        await using var site = await LocalSite.StartAsync(app =>
            app.MapGet("/missing", () => Microsoft.AspNetCore.Http.Results.Content("<html><body><p>Not here, sorry about that.</p></body></html>", "text/html", null, 404)));
        await using var fetcher = LocalFetcher(o => o.MaxHtmlBytes = 20_000);

        var missing = await fetcher.FetchAsync(new Uri(site.Url("/missing")), CancellationToken.None);
        Assert.Equal(404, missing.StatusCode);

        var big = await fetcher.FetchAsync(new Uri(site.Url("/big")), CancellationToken.None);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(big.Html) <= 20_000);

        var cookie = await fetcher.FetchAsync(new Uri(site.Url("/cookie-set")), CancellationToken.None);
        Assert.Contains("Cookie arrived", cookie.Html);
    }
}
