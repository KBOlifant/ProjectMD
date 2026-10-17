using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectMD.Api.Services;
using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Extraction.Visual;

namespace ProjectMD.Api.Tests.Extraction;

// Tests for the Motion settings, the contact-sheet layout, image loading in the request proxy and
// (opt-in, needs a browser) the whole visual capture against a local animated page.
[Collection("Playwright")]
public class VisualCaptureTests
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders = new Dictionary<string, string>();

    // ---- Settings ---------------------------------------------------------------------------------

    [Fact]
    public void Motion_is_off_by_default_so_old_scans_are_unchanged()
    {
        var options = new MotionOptions();

        Assert.False(options.Enabled);
        Assert.Equal(1440, options.ViewportWidth);
        Assert.Equal(6, options.ScrollFrames);
        Assert.Equal("qwen/qwen3.8-27b", options.VisionModel);
        Assert.Equal(3, options.MaxImagesPerRequest);
        Assert.True(MotionOptions.IsValid(options));
    }

    [Theory]
    [InlineData(nameof(MotionOptions.ScrollFrames), 0)]
    [InlineData(nameof(MotionOptions.ScreenshotQuality), 101)]
    [InlineData(nameof(MotionOptions.ViewportWidth), 100)]
    [InlineData(nameof(MotionOptions.MaxImagesPerRequest), 0)]
    public void Out_of_range_values_are_invalid(string property, int value)
    {
        var options = new MotionOptions();
        typeof(MotionOptions).GetProperty(property)!.SetValue(options, value);

        Assert.False(MotionOptions.IsValid(options));
    }

    [Fact]
    public void Unknown_reasoning_effort_is_invalid()
    {
        Assert.False(MotionOptions.IsValid(new MotionOptions { ReasoningEffort = "maximum" }));
    }

    // ---- Contact sheets ---------------------------------------------------------------------------

    [Fact]
    public void Frames_are_split_into_sheets_of_columns_times_rows()
    {
        var options = new MotionOptions { SheetColumns = 3, SheetRows = 2 };

        var pages = ContactSheet.Pages(8, options);

        Assert.Equal(2, pages.Count);
        Assert.Equal([0, 1, 2, 3, 4, 5], pages[0]);
        Assert.Equal([6, 7], pages[1]);
    }

    [Fact]
    public void Captions_say_how_far_and_how_long_after_the_first_frame()
    {
        var options = new MotionOptions { ScrollStepPixels = 760, FrameDelayMs = 700 };

        Assert.Equal("Frame 1 - scrolled 0 px - 0.0 s", ContactSheet.Caption(0, options));
        Assert.Equal("Frame 3 - scrolled 1520 px - 1.4 s", ContactSheet.Caption(2, options));
    }

    [Fact]
    public void Sheet_html_has_one_image_per_frame_and_loads_nothing_from_outside()
    {
        var options = new MotionOptions();
        byte[][] frames = [[1, 2, 3], [4, 5, 6], [7, 8, 9]];

        var html = ContactSheet.BuildHtml(frames, [0, 1, 2], options);

        Assert.Equal(3, CountOf(html, "<img "));
        Assert.Contains("data:image/jpeg;base64," + Convert.ToBase64String(frames[1]), html);
        Assert.DoesNotContain("http", html);
        Assert.DoesNotContain("<script", html);
    }

    // ---- Image loading in the proxy ---------------------------------------------------------------

    [Fact]
    public async Task Images_and_fonts_are_fetched_when_a_visual_budget_is_given()
    {
        await using var site = await LocalSite.StartAsync();
        var dns = LocalSite.Dns();
        var guard = LocalSite.Guard(dns);
        using var client = new GuardedHttpClient(guard);
        var proxy = new PageRequestProxy(client, guard, new ExtractionOptions { RequestTimeoutSeconds = 5 }, null, visualAssetBytes: 1_000_000, visualAssetRequests: 10);

        var image = await proxy.HandleAsync(Get(site.Url("/site.css"), "image"), CancellationToken.None);
        var font = await proxy.HandleAsync(Get(site.Url("/site.css"), "font"), CancellationToken.None);
        var media = await proxy.HandleAsync(Get(site.Url("/site.css"), "media"), CancellationToken.None);

        Assert.IsType<ProxyOutcome.Fulfil>(image);
        Assert.IsType<ProxyOutcome.Fulfil>(font);
        Assert.IsType<ProxyOutcome.Abort>(media);
        Assert.Equal(2, site.Hits.Count);
    }

    [Fact]
    public async Task Image_requests_stop_at_their_own_request_limit()
    {
        await using var site = await LocalSite.StartAsync();
        var dns = LocalSite.Dns();
        var guard = LocalSite.Guard(dns);
        using var client = new GuardedHttpClient(guard);
        var proxy = new PageRequestProxy(client, guard, new ExtractionOptions { RequestTimeoutSeconds = 5 }, null, visualAssetBytes: 1_000_000, visualAssetRequests: 1);

        var first = await proxy.HandleAsync(Get(site.Url("/site.css"), "image"), CancellationToken.None);
        var second = await proxy.HandleAsync(Get(site.Url("/site.css"), "image"), CancellationToken.None);
        var script = await proxy.HandleAsync(Get(site.Url("/site.css"), "script"), CancellationToken.None);

        Assert.IsType<ProxyOutcome.Fulfil>(first);
        Assert.IsType<ProxyOutcome.Abort>(second);
        Assert.IsType<ProxyOutcome.Fulfil>(script);
    }

    [Fact]
    public async Task Images_still_pass_the_address_checks()
    {
        await using var site = await LocalSite.StartAsync();
        var dns = LocalSite.Dns();
        var guard = LocalSite.Guard(dns);
        using var client = new GuardedHttpClient(guard);
        var proxy = new PageRequestProxy(client, guard, new ExtractionOptions { RequestTimeoutSeconds = 5 }, null, visualAssetBytes: 1_000_000, visualAssetRequests: 10);

        var outcome = await proxy.HandleAsync(Get("http://internal.test/secret.png", "image"), CancellationToken.None);

        Assert.IsType<ProxyOutcome.Abort>(outcome);
        Assert.Empty(site.Hits);
    }

    // ---- The whole capture (opt-in) ---------------------------------------------------------------

    private const string AnimatedPage = """
        <html><head><title>Animated</title>
        <style>
          body { margin: 0; font-family: Arial, sans-serif; }
          section { height: 900px; display: flex; align-items: center; justify-content: center; font-size: 48px; }
          .spin { width: 80px; height: 80px; background: #e11d48; animation: spin 1.5s linear infinite; }
          @keyframes spin { to { transform: rotate(360deg); } }
        </style></head>
        <body>
          <section style="background:#fde68a"><div class="spin" id="spinner"></div></section>
          <section style="background:#bfdbfe">Second screen</section>
          <section style="background:#bbf7d0">Third screen</section>
          <section style="background:#fbcfe8">Fourth screen</section>
        </body></html>
        """;

    [BrowserFact]
    public async Task Captures_frames_sheets_and_a_measured_css_animation()
    {
        var channel = Environment.GetEnvironmentVariable("PROJECTMD_BROWSER_CHANNEL") ?? string.Empty;
        var options = new ExtractionOptions { BrowserChannel = channel, RequestTimeoutSeconds = 5, NavigationTimeoutSeconds = 15, OverallTimeoutSeconds = 60 };
        var motion = new MotionOptions { Enabled = true, ScrollFrames = 3, FrameDelayMs = 200, ViewportWidth = 1024, ViewportHeight = 768 };
        await using var site = await LocalSite.StartAsync(app => app.MapGet("/animated", () => Results.Content(AnimatedPage, "text/html")));
        var dns = LocalSite.Dns();
        await using var fetcher = new PlaywrightPageFetcher(
            new UrlValidator(),
            () => LocalSite.Guard(dns, options.MaxRedirects),
            Options.Create(options),
            NullLogger<PlaywrightPageFetcher>.Instance,
            Options.Create(motion));

        var page = await fetcher.FetchAsync(new Uri(site.Url("/animated")), CancellationToken.None);

        var visual = page.Visual;
        Assert.NotNull(visual);
        Assert.Equal(3, visual.Frames.Count);
        Assert.All(visual.Frames, f => Assert.True(f.Length > 1000 && f[0] == 0xFF && f[1] == 0xD8, "frames are JPEG images"));
        Assert.Single(visual.Sheets);
        Assert.NotNull(visual.Motion);
        Assert.Contains(visual.Motion.Animations, a => a.Source == "css-animation" && a.Name == "spin" && a.DurationMs == 1500);
    }

    private static ProxyRequest Get(string url, string type) => new("GET", new Uri(url), NoHeaders, null, type, false);

    private static int CountOf(string text, string part)
    {
        var count = 0;
        for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
