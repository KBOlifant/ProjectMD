using ProjectMD.Api.Models;
using ProjectMD.Api.Services.Extraction;
using static ProjectMD.Api.Tests.Extraction.ExtractionTestSupport;

namespace ProjectMD.Api.Tests.Extraction;

// Tests for the technology fingerprinter and its rules file (digests only, no network).
public class TechFingerprinterTests
{
    private static readonly TechRuleSet Rules = TechRuleSet.LoadEmbedded();

    private readonly TechFingerprinter _fingerprinter = new(Rules);

    // The names of the detected technologies, in order.
    private static List<string> Names(IReadOnlyList<ScanTech> tech) => tech.Select(t => t.Name).ToList();

    // The shipped rules file loads, covers 40+ technologies and every one can be proven by a
    // strong signal.
    [Fact]
    public void Embedded_rules_load_and_are_well_formed()
    {
        Assert.True(Rules.Rules.Count >= 40, $"only {Rules.Rules.Count} rules");
        Assert.All(Rules.Rules, rule =>
        {
            Assert.Contains(rule.Category, TechRuleSet.Categories);
            Assert.Contains(rule.Signals, s => s.Strong);
        });

        var names = Rules.Rules.Select(r => r.Name).ToList();
        foreach (var expected in new[]
                 {
                     "React", "Next.js", "Vue.js", "Nuxt", "Angular", "Svelte", "WordPress", "Shopify", "Webflow", "Wix",
                     "Squarespace", "Tailwind CSS", "Bootstrap", "jQuery", "Vercel", "Netlify", "Cloudflare", "Fastly",
                     "AWS CloudFront", "Google Analytics", "Google Tag Manager", "Segment", "PostHog", "Hotjar", "Stripe",
                     "Intercom", "HubSpot", "Algolia", "Clerk", "Firebase", "Sentry",
                 })
        {
            Assert.Contains(expected, names);
        }
    }

    // A Next.js site on Vercel with Tailwind: framework and host first (highlighted), React implied.
    [Fact]
    public void Detects_nextjs_on_vercel_with_tailwind()
    {
        var digest = Digest(
            scripts: ["https://shop.example.com/_next/static/chunks/main-abc123.js"],
            headers: new Dictionary<string, string> { ["x-vercel-id"] = "iad1::abcde-1234", ["server"] = "Vercel" },
            markers: ["class:tailwind", "id:__next", "js:__NEXT_DATA__"]);

        var tech = _fingerprinter.Detect(digest);

        Assert.Equal(["Next.js", "Vercel", "React", "Tailwind CSS"], Names(tech));
        Assert.Equal([true, true, false, false], tech.Select(t => t.Highlighted));
    }

    // WordPress behind Cloudflare with jQuery and Google Analytics, found through addresses,
    // headers, cookie names and the generator tag.
    [Fact]
    public void Detects_wordpress_cloudflare_jquery_and_analytics()
    {
        var digest = Digest(
            scripts:
            [
                "https://blog.example.com/wp-includes/js/jquery/jquery.min.js?ver=3.7.1",
                "https://www.googletagmanager.com/gtag/js?id=G-ABC",
            ],
            styles: ["https://blog.example.com/wp-content/themes/x/style.css"],
            headers: new Dictionary<string, string>
            {
                ["Server"] = "cloudflare",
                ["CF-Ray"] = "8a1b2c3d4e5f-AMS",
                ["set-cookie"] = "__cf_bm, _ga",
            },
            markers: ["generator:WordPress 6.4.2"]);

        var names = Names(_fingerprinter.Detect(digest));

        Assert.Equal("WordPress", names[0]);
        Assert.Equal("Cloudflare", names[1]);
        Assert.Contains("jQuery", names);
        Assert.Contains("Google Analytics", names);
    }

    // Shopify through its CDN and a marker from an inline script.
    [Fact]
    public void Detects_shopify()
    {
        var names = Names(_fingerprinter.Detect(Digest(
            scripts: ["https://cdn.shopify.com/s/files/1/0000/theme.js"],
            markers: ["js:Shopify."])));

        Assert.Equal(["Shopify"], names);
    }

    // Nuxt brings Vue.js with it.
    [Fact]
    public void Implied_technologies_are_added()
    {
        var names = Names(_fingerprinter.Detect(Digest(scripts: ["https://x.example.com/_nuxt/entry.js"])));

        Assert.Equal(["Nuxt", "Vue.js"], names);
    }

    // Header names are matched ignoring case.
    [Fact]
    public void Header_names_are_case_insensitive()
    {
        var names = Names(_fingerprinter.Detect(Digest(headers: new Dictionary<string, string> { ["X-VERCEL-ID"] = "x" })));

        Assert.Equal(["Vercel"], names);
    }

    // A page with no known signals gets an empty list.
    [Fact]
    public void Unknown_page_has_no_technologies()
    {
        var tech = _fingerprinter.Detect(Digest(
            scripts: ["https://example.com/static/custom-bundle.js"],
            styles: ["https://example.com/static/custom.css"],
            headers: new Dictionary<string, string> { ["server"] = "nginx" },
            text: "A plain page about nothing in particular."));

        Assert.Empty(tech);
    }

    // Weak signals alone never report a technology.
    [Fact]
    public void Weak_signals_alone_are_not_enough()
    {
        // js:dataLayer is a weak Google Tag Manager hint, id:root and id:app are weak React/Vue hints.
        var tech = _fingerprinter.Detect(Digest(markers: ["js:dataLayer", "id:root", "id:app", "js:gtag("]));

        Assert.Empty(tech);
    }

    // Weak signals add to a technology that has a strong one, but cannot create one.
    [Fact]
    public void Plain_react_root_without_strong_signal_is_not_react()
    {
        Assert.DoesNotContain("React", Names(_fingerprinter.Detect(Digest(markers: ["id:root"]))));
        Assert.Contains("React", Names(_fingerprinter.Detect(Digest(markers: ["id:root", "attr:data-reactroot"]))));
    }

    // At most 8 technologies are returned.
    [Fact]
    public void Result_is_capped_at_eight()
    {
        var digest = Digest(
            scripts:
            [
                "https://cdn.segment.com/analytics.js/v1/abc/analytics.min.js",
                "https://static.hotjar.com/c/hotjar-1.js",
                "https://js.stripe.com/v3/",
                "https://widget.intercom.io/widget/abc",
                "https://js.hs-scripts.com/123.js",
                "https://browser.sentry-cdn.com/7/bundle.min.js",
                "https://code.jquery.com/jquery-3.7.1.min.js",
                "https://us-assets.i.posthog.com/static/array.js",
                "https://www.google-analytics.com/analytics.js",
            ],
            styles: ["https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css", "https://fonts.googleapis.com/css2?family=Inter"],
            headers: new Dictionary<string, string> { ["cf-ray"] = "abc", ["x-vercel-id"] = "abc" },
            markers: ["generator:WordPress 6.0"]);

        var tech = _fingerprinter.Detect(digest);

        Assert.Equal(TechFingerprinter.MaxResults, tech.Count);
        Assert.Equal(8, tech.Count);
        Assert.Equal("WordPress", tech[0].Name);
        Assert.Equal("Vercel", tech[1].Name);
        Assert.Equal(2, tech.Count(t => t.Highlighted));
    }

    // Only the framework/platform and the host/CDN are highlighted; analytics never are.
    [Fact]
    public void Analytics_only_pages_highlight_nothing()
    {
        var tech = _fingerprinter.Detect(Digest(scripts: ["https://static.hotjar.com/c/hotjar-1.js", "https://js.stripe.com/v3/"]));

        Assert.Equal(2, tech.Count);
        Assert.All(tech, t => Assert.False(t.Highlighted));
    }

    // Detection works from a reduced real-looking page, end to end (reducer then fingerprinter).
    [Fact]
    public void Detects_from_a_reduced_page()
    {
        var html = "<html><head><meta name=\"generator\" content=\"Hugo 0.120\"><script src=\"/_next/static/chunks/app.js\"></script>" +
                   "<link rel=\"stylesheet\" href=\"https://fonts.googleapis.com/css2?family=Inter\"></head>" +
                   "<body><div id=\"__next\"><main><h1>Hello world</h1><p>" + new string('x', 250) + "</p></main></div></body></html>";

        var tech = Names(_fingerprinter.Detect(Reduce(html)));

        Assert.Contains("Next.js", tech);
        Assert.Contains("Hugo", tech);
        Assert.Contains("React", tech);
        Assert.Contains("Google Fonts", tech);
    }

    // The rules file validator rejects bad rules with a clear error.
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"technologies\":[{\"name\":\"A\",\"category\":\"nope\",\"signals\":[{\"type\":\"script\",\"pattern\":\"x\",\"strong\":true}]}]}")]
    [InlineData("{\"technologies\":[{\"name\":\"A\",\"category\":\"css\",\"signals\":[{\"type\":\"script\",\"pattern\":\"x\",\"strong\":false}]}]}")]
    [InlineData("{\"technologies\":[{\"name\":\"A\",\"category\":\"css\",\"signals\":[{\"type\":\"telepathy\",\"pattern\":\"x\",\"strong\":true}]}]}")]
    [InlineData("{\"technologies\":[{\"name\":\"A\",\"category\":\"css\",\"signals\":[{\"type\":\"script\",\"pattern\":\"(\",\"strong\":true}]}]}")]
    [InlineData("{\"technologies\":[{\"name\":\"A\",\"category\":\"css\",\"implies\":[\"Ghost\"],\"signals\":[{\"type\":\"script\",\"pattern\":\"x\",\"strong\":true}]}]}")]
    [InlineData("{\"technologies\":[{\"name\":\"A\",\"category\":\"css\",\"signals\":[{\"type\":\"marker\",\"strong\":true}]}]}")]
    [InlineData("not json")]
    public void Invalid_rule_files_are_rejected(string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.Throws<InvalidDataException>(() => TechRuleSet.Load(stream));
    }

    // A small custom rules file works: shows the engine is data-driven.
    [Fact]
    public void Custom_rules_are_used()
    {
        const string json = """
            {"technologies":[{"name":"Acme CMS","category":"platform","signals":[{"type":"text","pattern":"Powered by Acme","strong":true}]}]}
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var fingerprinter = new TechFingerprinter(TechRuleSet.Load(stream));

        var tech = fingerprinter.Detect(Digest(text: "Footer: Powered by Acme CMS"));

        var item = Assert.Single(tech);
        Assert.Equal("Acme CMS", item.Name);
        Assert.True(item.Highlighted);
    }
}
