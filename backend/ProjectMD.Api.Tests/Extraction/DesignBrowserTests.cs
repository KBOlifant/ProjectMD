using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectMD.Api.Services;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Tests.Extraction;

// Opt-in test (PROJECTMD_BROWSER_TESTS=1, needs an installed browser) of the whole design capture
// against a local page whose styles are known: the in-page script, the stylesheet text taken from
// the request proxy, and the summary.
[Collection("Playwright")]
public class DesignBrowserTests
{
    private const string Page = """
        <html><head><title>Styled</title>
        <link rel="stylesheet" href="/design.css">
        <style>:root { --inline-accent: #ff6600; }
        body { margin: 0; background: #ffffff; color: #111827; font-family: Georgia, serif; font-size: 16px; }
        .box { margin: 24px; padding: 16px; border: 1px solid #e5e7eb; border-radius: 12px; background: #f9fafb; box-shadow: 0 4px 6px rgba(0,0,0,0.1); }
        .cta { display: inline-block; padding: 8px 16px; border-radius: 6px; background: #3b82f6; color: #ffffff; text-decoration: none; }
        .modern1 { background: oklch(0.65 0.25 30); color: oklab(0.95 0 0); padding: 8px; }
        .modern2 { background: color(display-p3 0.1 0.7 0.3); color: lch(30 60 140); padding: 8px; }
        .pill { background: #123456; border-radius: 33554400px; padding: 4px; box-shadow: rgba(0, 0, 0, 0) 0px 0px 0px 0px, rgba(0, 0, 0, 0) 0px 0px 0px 0px; }
        </style></head>
        <body><main><div class="box"><h1>Hello design</h1><p>Some text to give the page a body and a few styles.</p>
        <a class="cta" href="/x">Click me</a></div>
        <div class="modern1">Modern one</div><div class="modern2">Modern two</div><span class="pill">Pill</span></main></body></html>
        """;

    private const string Css = """
        :root { --brand-color: #3b82f6; --spacing-md: 16px; }
        @media (min-width: 640px) { .box { padding: 24px; } }
        @media (max-width: 900px) { .cta { padding: 4px; } }
        """;

    // Fetches the styled page and checks the summary against what the page really uses.
    [BrowserFact]
    public async Task Captures_the_known_styles_of_a_local_page()
    {
        var channel = Environment.GetEnvironmentVariable("PROJECTMD_BROWSER_CHANNEL") ?? string.Empty;
        var options = new ExtractionOptions { BrowserChannel = channel, RequestTimeoutSeconds = 5, NavigationTimeoutSeconds = 15, OverallTimeoutSeconds = 40 };
        await using var site = await LocalSite.StartAsync(app =>
        {
            app.MapGet("/styled", () => Results.Content(Page, "text/html"));
            app.MapGet("/design.css", () => Results.Content(Css, "text/css"));
        });
        var dns = LocalSite.Dns();
        await using var fetcher = new PlaywrightPageFetcher(
            new UrlValidator(), () => LocalSite.Guard(dns, options.MaxRedirects), Options.Create(options), NullLogger<PlaywrightPageFetcher>.Instance);

        var page = await fetcher.FetchAsync(new Uri(site.Url("/styled")), CancellationToken.None);

        var design = page.Design;
        Assert.NotNull(design);
        Assert.Contains(design.Colors, c => c.Hex == "#111827");
        Assert.Contains(design.Colors, c => c.Hex == "#3b82f6");
        Assert.Contains(design.Colors, c => c.Hex == "#ffffff");
        Assert.Contains(design.Colors, c => c.Hex == "#e5e7eb" && c.Role == "border");
        Assert.Contains(design.Fonts, f => f.Family == "Georgia" && f.Sizes.Contains("16px"));
        // oklch / oklab / lch / display-p3 colours are resolved to sRGB in the page, not dropped.
        Assert.True(design.Colors.Count(c => Services.Extraction.Design.ColorParser.Saturation(c.Hex) > 0.4) >= 4);
        Assert.Contains("9999px", design.Radii);
        Assert.DoesNotContain(design.Radii, r => r.Contains('e'));
        Assert.DoesNotContain(design.Shadows, s => s.Contains("rgba(0, 0, 0, 0)"));
        Assert.Contains("12px", design.Radii);
        Assert.Contains("6px", design.Radii);
        Assert.Contains(design.Shadows, s => s.Contains("0px 4px 6px"));
        Assert.Contains("16px", design.SpacingScale);
        Assert.Contains(new CssVariable("--brand-color", "#3b82f6"), design.CssVariables);
        Assert.Contains(new CssVariable("--inline-accent", "#ff6600"), design.CssVariables);
        Assert.Equal(["640px", "900px"], design.Breakpoints);
        Assert.Null(design.UtilityFramework);
        Assert.True(Services.Extraction.Design.DesignAnalyzer.SerialisedBytes(design) < 3000);
    }
}
