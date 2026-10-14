using System.Text.Json;
using ProjectMD.Api.Services.Extraction;
using static ProjectMD.Api.Tests.Extraction.ExtractionTestSupport;

namespace ProjectMD.Api.Tests.Extraction;

// Tests for the HTML reducer, using fixture HTML only (no network, no browser).
public class HtmlReducerTests
{
    // Scripts, styles, svg, iframes, noscript, comments, hidden elements and data URIs are gone;
    // the visible paragraph text stays.
    [Fact]
    public void Removes_noise_and_keeps_visible_text()
    {
        var digest = Reduce(SamplePage());

        Assert.Contains("Acme lets you build reliable anvils quickly.", digest.Text);
        Assert.Contains("Run the installer and follow the prompts", digest.Text);

        foreach (var junk in new[]
                 {
                     "window.__STATE__", "color: red", "a comment that must disappear", "Hidden paragraph",
                     "Aria hidden", "Display none", "icon title", "iVBORw0KGgo", "Please enable JavaScript",
                     "ads.example.net", "We use cookies", "Accept all cookies",
                 })
        {
            Assert.DoesNotContain(junk, digest.Text);
            Assert.DoesNotContain(junk, string.Join(" ", digest.Outline.Select(h => h.Text)));
        }
    }

    // Headings go to the outline (h1 to h4 only), not into the body text.
    [Fact]
    public void Outline_has_h1_to_h4_in_document_order_and_not_h5()
    {
        var digest = Reduce(SamplePage());

        Assert.Equal(
            [new HeadingNode(1, "Getting started"), new HeadingNode(2, "Install"), new HeadingNode(3, "Requirements")],
            digest.Outline);
        Assert.DoesNotContain("Not in the outline", digest.Text);
    }

    // A heading in the footer (page furniture) does not belong to the outline.
    [Fact]
    public void Outline_skips_footer_and_nav_headings()
    {
        var digest = Reduce(SamplePage());

        Assert.DoesNotContain(digest.Outline, h => h.Text == "Company");
    }

    // The outline is capped at MaxHeadings.
    [Fact]
    public void Outline_is_capped()
    {
        var html = "<body><main>" + string.Concat(Enumerable.Range(1, 30).Select(i => $"<h2>Heading number {i}</h2>")) + "<p>" + new string('a', 300) + "</p></main></body>";

        var digest = Reduce(html, new ReduceOptions(MaxHeadings: 5));

        Assert.Equal(5, digest.Outline.Count);
        Assert.Equal("Heading number 1", digest.Outline[0].Text);
    }

    // Title, description, language and (as the fallback) Open Graph values are read.
    [Fact]
    public void Reads_title_description_and_language()
    {
        var digest = Reduce(SamplePage());

        Assert.Equal("Acme Docs | Getting started", digest.Title);
        Assert.Equal("Everything you need to start building with Acme.", digest.MetaDescription);
        Assert.Equal("en-GB", digest.Language);
        Assert.Contains("og:site_name=Acme", digest.Markers!);
    }

    // With no <title> or description, the Open Graph values are used.
    [Fact]
    public void Falls_back_to_open_graph()
    {
        var digest = Reduce("<html><head><meta property=\"og:title\" content=\"OG Title\"><meta property=\"og:description\" content=\"OG description\"><meta property=\"og:locale\" content=\"fr_FR\"></head><body><p>Some real body text for the page.</p></body></html>");

        Assert.Equal("OG Title", digest.Title);
        Assert.Equal("OG description", digest.MetaDescription);
        Assert.Equal("fr-FR", digest.Language);
    }

    // JSON-LD is kept, parsed and compacted (junk removed); invalid JSON-LD is dropped.
    [Fact]
    public void JsonLd_is_compacted_and_invalid_blocks_are_dropped()
    {
        var digest = Reduce(SamplePage());

        var block = Assert.Single(digest.JsonLd);
        using var json = JsonDocument.Parse(block);
        Assert.Equal("Organization", json.RootElement.GetProperty("@type").GetString());
        Assert.Equal("Acme", json.RootElement.GetProperty("name").GetString());
        Assert.False(json.RootElement.TryGetProperty("@context", out _));
        Assert.False(json.RootElement.TryGetProperty("potentialAction", out _));
        Assert.False(json.RootElement.TryGetProperty("empty", out _));
        Assert.DoesNotContain("\n", block);
        Assert.DoesNotContain(": ", block);
    }

    // Links: absolute, deduplicated by address, noise removed, text required, image alt as fallback.
    [Fact]
    public void Links_are_absolute_deduplicated_and_noise_is_dropped()
    {
        var digest = Reduce(SamplePage());
        var hrefs = digest.Links.Select(l => l.Href).ToList();

        Assert.Contains("https://www.example-site.com/docs/install", hrefs);
        Assert.Contains("https://www.example-site.com/docs/usage", hrefs);
        Assert.Contains("https://github.com/acme/acme", hrefs);
        Assert.Contains("https://www.example-site.com/legal/privacy", hrefs);
        Assert.Contains("https://www.example-site.com/", hrefs);

        // One entry for /docs/usage even though it is linked three times (one with a #fragment).
        Assert.Single(hrefs, h => h == "https://www.example-site.com/docs/usage");
        Assert.DoesNotContain(hrefs, h => h.StartsWith("mailto:") || h.StartsWith("javascript:") || h.Contains('#'));

        // The link to the page itself (/docs/ by image alt) is dropped.
        Assert.DoesNotContain(digest.Links, l => l.Text == "Docs home");

        // Internal links come before external ones.
        var github = hrefs.IndexOf("https://github.com/acme/acme");
        Assert.True(github > hrefs.IndexOf("https://www.example-site.com/docs/install"));
        Assert.True(github > hrefs.IndexOf("https://www.example-site.com/legal/privacy"));
    }

    // A <base href> changes how relative links resolve; a link with only an image uses the alt text.
    [Fact]
    public void Links_use_base_href_and_image_alt()
    {
        var html = "<html><head><base href=\"https://other.example.org/root/\"></head><body><p>" + new string('w', 60) + "</p><a href=\"page\"><img alt=\"Alt words\" src=\"x.png\"></a></body></html>";

        var digest = Reduce(html);

        var link = Assert.Single(digest.Links);
        Assert.Equal("https://other.example.org/root/page", link.Href);
        Assert.Equal("Alt words", link.Text);
    }

    // Links are capped.
    [Fact]
    public void Links_are_capped()
    {
        var html = "<body><main><p>" + new string('w', 300) + "</p>" + string.Concat(Enumerable.Range(1, 100).Select(i => $"<a href=\"/p/{i}\">Page {i}</a>")) + "</main></body>";

        var digest = Reduce(html, new ReduceOptions(MaxLinks: 10));

        Assert.Equal(10, digest.Links.Count);
        Assert.Equal("https://www.example-site.com/p/1", digest.Links[0].Href);
    }

    // The text comes from <main> when it has real content, so navigation and footer are left out.
    [Fact]
    public void Prefers_main_content()
    {
        var digest = Reduce(SamplePage());

        Assert.DoesNotContain("Copyright Acme Inc.", digest.Text);
        Assert.DoesNotContain("Privacy", digest.Text);
    }

    // Without <main>, furniture (nav, footer) is still included but ranked last.
    [Fact]
    public void Without_main_the_body_is_used_and_furniture_is_last_priority()
    {
        var content = string.Concat(Enumerable.Range(1, 30).Select(i => $"<div><p>Real content sentence number {i} for the reader.</p></div>"));
        var html = "<body><nav><ul><li>Navigation item alpha with a label that is long enough to count</li><li>Navigation item beta with another long enough label</li></ul></nav>" +
                   content +
                   "<footer><p>Footer legal text that goes on and on and on forever and ever.</p></footer></body>";

        var roomy = Reduce(html, new ReduceOptions(MaxTextChars: 5000));
        Assert.Contains("Navigation item alpha", roomy.Text);
        Assert.Contains("Footer legal text", roomy.Text);

        var tight = Reduce(html, new ReduceOptions(MaxTextChars: 500));
        Assert.Contains("Real content sentence number 1 for the reader.", tight.Text);
        Assert.DoesNotContain("Navigation item alpha", tight.Text);
        Assert.DoesNotContain("Footer legal text", tight.Text);
        Assert.True(tight.Text.Length <= 500);
    }

    // The same text twice (repeated nav or footer) is kept once; tiny lines are dropped.
    [Fact]
    public void Duplicate_blocks_and_tiny_lines_are_dropped()
    {
        var html = "<body><main><p>Shared sentence that appears twice on the page.</p><p>ok</p><p>-</p><p>A</p><p>Unique sentence only once here.</p><p>Shared sentence that appears twice on the page.</p><p>" + new string('z', 200) + " end</p></main></body>";

        var digest = Reduce(html);
        var lines = digest.Text.Split('\n');

        Assert.Single(lines, l => l == "Shared sentence that appears twice on the page.");
        Assert.DoesNotContain(lines, l => l.Length < 3);
        Assert.Contains("Unique sentence only once here.", lines);
    }

    // Cookie banners are removed by container name and by phrase.
    [Fact]
    public void Cookie_banners_are_removed()
    {
        var html = "<body><main><p>Genuine page content that readers care about, in a sentence.</p>" +
                   "<p>We use cookies to give you the best experience on this site.</p>" +
                   "<div class=\"cc-window\">Pick your preferences now please friends.</div></main></body>";

        var digest = Reduce(html);

        Assert.Contains("Genuine page content", digest.Text);
        Assert.DoesNotContain("cookies", digest.Text);
        Assert.DoesNotContain("preferences", digest.Text);
    }

    // Table rows become one line with cells joined by " | ".
    [Fact]
    public void Table_rows_become_single_lines()
    {
        var digest = Reduce(SamplePage());

        Assert.Contains("Plan | Price", digest.Text);
        Assert.Contains("Basic | Free forever", digest.Text);
    }

    // The reduced page is at least 90% smaller than the raw HTML.
    [Fact]
    public void Reduces_a_heavy_page_by_at_least_90_percent()
    {
        var html = HeavyPage();

        var digest = Reduce(html);

        Assert.Equal(html.Length, digest.RawHtmlChars);
        Assert.True(digest.ReducedChars * 10 <= digest.RawHtmlChars, $"raw {digest.RawHtmlChars} reduced {digest.ReducedChars}");
        Assert.Contains("one paragraph of real content", digest.Text);
    }

    // EstimatedTokens is ceil(ReducedChars / 4).
    [Fact]
    public void Estimated_tokens_is_chars_divided_by_four_rounded_up()
    {
        var digest = Reduce(SamplePage());

        Assert.Equal((digest.ReducedChars + 3) / 4, digest.EstimatedTokens);
        Assert.True(digest.ReducedChars >= digest.Text.Length);
    }

    // The hash is 64 lower-case hex characters.
    [Fact]
    public void Content_hash_is_sha256_hex()
    {
        var digest = Reduce(SamplePage());

        Assert.Matches("^[0-9a-f]{64}$", digest.ContentHash);
    }

    // Nonces, CSRF tokens, session ids and timestamps do not change the hash.
    [Fact]
    public void Content_hash_is_stable_when_only_volatile_bits_change()
    {
        var first = Reduce(SamplePage(nonce: "n-111", session: "9f2c4b7a1d3e5f60"));
        var second = Reduce(SamplePage(nonce: "n-999", session: "0a1b2c3d4e5f6789"));

        Assert.Equal(first.ContentHash, second.ContentHash);

        var withStamp1 = Reduce("<body><main><p>Last updated 2026-03-04T10:11:12Z by the editors of this page.</p><p>Request id: 7f3a9c1e5b2d4f60</p><p>Real stable content lives here for readers.</p></main></body>");
        var withStamp2 = Reduce("<body><main><p>Last updated 2026-09-30T23:59:01Z by the editors of this page.</p><p>Request id: 1a2b3c4d5e6f7081</p><p>Real stable content lives here for readers.</p></main></body>");
        Assert.Equal(withStamp1.ContentHash, withStamp2.ContentHash);
    }

    // A real change of content changes the hash; so does the title.
    [Fact]
    public void Content_hash_changes_when_content_changes()
    {
        var original = Reduce(SamplePage());
        var edited = Reduce(SamplePage().Replace("reliable anvils", "reliable hammers"));
        var retitled = Reduce(SamplePage().Replace("Getting started  </title>", "Getting going  </title>"));

        Assert.NotEqual(original.ContentHash, edited.ContentHash);
        Assert.NotEqual(original.ContentHash, retitled.ContentHash);
    }

    // The text never exceeds the budget, the budget is used by whole sentences/blocks or a
    // word-boundary cut, and higher priority content is kept first.
    [Fact]
    public void Truncation_respects_budget_priority_and_word_boundaries()
    {
        var paragraphs = string.Concat(Enumerable.Range(1, 40).Select(i => $"<p>Paragraph {i} has plenty of ordinary words in it to make it a decent length.</p>"));
        var html = "<body><nav><ul><li>Home page link</li><li>About page link</li></ul></nav><main>" + paragraphs + "</main><footer><p>Footer text that should lose to the content.</p></footer></body>";

        var digest = Reduce(html, new ReduceOptions(MaxTextChars: 700));

        Assert.True(digest.Text.Length <= 700, $"length {digest.Text.Length}");
        Assert.Contains("Paragraph 1 has plenty", digest.Text);
        Assert.DoesNotContain("Footer text", digest.Text);

        // Every line is a whole paragraph, or a paragraph cut at a word followed by an ellipsis.
        foreach (var line in digest.Text.Split('\n'))
        {
            Assert.Matches(@"^Paragraph \d+ has plenty of ordinary words in it to make it a decent length\.$|^Paragraph \d+ has( [a-z]+)*…$", line);
        }
    }

    // A single long block is cut at a word boundary, with an ellipsis, inside the budget.
    [Fact]
    public void A_single_long_block_is_cut_at_a_word()
    {
        var words = string.Join(' ', Enumerable.Range(1, 500).Select(i => $"word{i}"));
        var digest = Reduce($"<body><main><p>{words}</p></main></body>", new ReduceOptions(MaxTextChars: 600));

        Assert.True(digest.Text.Length <= 600);
        Assert.EndsWith("…", digest.Text);
        Assert.Matches(@"word\d+…$", digest.Text);
        Assert.DoesNotMatch(@"\bwor…$", digest.Text);
    }

    // Script and stylesheet addresses from the browser and from the markup are merged and absolute.
    [Fact]
    public void Script_and_stylesheet_urls_are_merged_without_duplicates()
    {
        var raw = Raw(
            SamplePage(),
            scripts: ["https://cdn.example.net/app.js", "https://cdn.example.net/extra.js"],
            styles: ["https://www.example-site.com/assets/site.css"]);

        var digest = new HtmlReducer().Reduce(raw, new ReduceOptions());

        Assert.Equal(["https://cdn.example.net/app.js", "https://cdn.example.net/extra.js"], digest.ScriptUrls);
        Assert.Equal(["https://www.example-site.com/assets/site.css"], digest.StylesheetUrls);
    }

    // The status code, final URL and headers are carried over.
    [Fact]
    public void Carries_status_url_and_headers()
    {
        var headers = new Dictionary<string, string> { ["Server"] = "nginx" };
        var digest = new HtmlReducer().Reduce(Raw("<body><p>Hello there, reader of pages.</p></body>", status: 203, headers: headers), new ReduceOptions());

        Assert.Equal(203, digest.StatusCode);
        Assert.Equal(PageUrl, digest.FinalUrl);
        Assert.Equal("nginx", digest.Headers["server"]);
    }

    // Markers: generator, root ids, framework attributes, inline script tokens and class families.
    [Fact]
    public void Markers_capture_framework_hints_from_the_markup()
    {
        var tailwind = string.Concat(Enumerable.Range(1, 8).Select(i => $"<div class=\"md:flex bg-gray-{i}00 text-sm font-bold hover:bg-blue-{i}00\"></div>"));
        var html = "<html><head><meta name=\"generator\" content=\"WordPress 6.4\">" +
                   "<script id=\"__NEXT_DATA__\" type=\"application/json\">{}</script>" +
                   "<script>window.dataLayer = []; Sentry.init({});</script></head>" +
                   "<body><div id=\"__next\" data-v-abc ng-version=\"17\"></div>" + tailwind + "</body></html>";

        var markers = Reduce(html).Markers!;

        Assert.Contains("generator:WordPress 6.4", markers);
        Assert.Contains("id:__next", markers);
        Assert.Contains("attr:data-v-", markers);
        Assert.Contains("attr:ng-version", markers);
        Assert.Contains("js:__NEXT_DATA__", markers);
        Assert.Contains("js:dataLayer", markers);
        Assert.Contains("js:Sentry.", markers);
        Assert.Contains("class:tailwind", markers);
        Assert.DoesNotContain("class:bootstrap", markers);
    }

    // Empty, odd and hostile input never throws.
    [Theory]
    [InlineData("")]
    [InlineData("not html at all")]
    [InlineData("<<<>>><p<div")]
    [InlineData("<html></html>")]
    [InlineData("<body><script>")]
    public void Odd_input_does_not_throw(string html)
    {
        var digest = Reduce(html);

        Assert.NotNull(digest.Text);
        Assert.Equal(64, digest.ContentHash.Length);
    }

    // A page of about 2 MB (the default HTML limit) reduces quickly and keeps the 90% saving.
    [Fact]
    public void A_two_megabyte_page_is_reduced_quickly()
    {
        var sb = new System.Text.StringBuilder("<html><head><title>Big</title></head><body><main><h1>Big page</h1>");
        for (var i = 0; sb.Length < 2_000_000; i++)
        {
            sb.Append($"<div class=\"card card-{i}\" data-id=\"{i}\" style=\"margin:{i}px\"><svg viewBox=\"0 0 10 10\"><path d=\"M0 0L10 10 M0 10L10 0 {new string('1', 200)}\"/></svg><span class=\"x\">{i}</span></div>");
            if (i % 200 == 0)
            {
                sb.Append($"<p>Paragraph number {i} with a little text in it so there is something to keep.</p>");
            }
        }

        sb.Append("</main></body></html>");
        var html = sb.ToString();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var digest = Reduce(html);

        watch.Stop();
        Assert.True(html.Length >= 2_000_000);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
        Assert.True(digest.ReducedChars * 10 <= digest.RawHtmlChars);
        Assert.True(digest.Text.Length <= 12000);
    }

    // Two spellings of one header name do not break the digest.
    [Fact]
    public void Duplicate_header_spellings_do_not_throw()
    {
        var headers = new Dictionary<string, string> { ["Server"] = "a", ["server"] = "b" };

        var digest = new HtmlReducer().Reduce(Raw("<body><p>Some body text here for the page.</p></body>", headers: headers), new ReduceOptions());

        Assert.True(digest.Headers.ContainsKey("SERVER"));
    }

    // Very deep nesting is handled (flattened) without a stack overflow.
    [Fact]
    public void Deeply_nested_markup_does_not_crash()
    {
        var html = "<body>" + string.Concat(Enumerable.Repeat("<div>", 20000)) + "<p>Deep paragraph of content that is buried deeply.</p>" + string.Concat(Enumerable.Repeat("</div>", 20000)) + "</body>";

        var digest = Reduce(html);

        Assert.Contains("Deep paragraph", digest.Text);
    }

    // A tiny page in the shape of example.com (a bare div with a heading, a paragraph and a link).
    [Fact]
    public void Reduces_a_minimal_page_with_a_bare_div()
    {
        const string html = """
            <html><head><title>Example Domain</title><meta charset="utf-8" />
            <style type="text/css">body { background-color: #f0f0f2; } div { width: 600px; }</style></head>
            <body><div>
                <h1>Example Domain</h1>
                <p>This domain is for use in documentation examples without needing permission. Avoid use in operations.</p>
                <p><a href="https://iana.org/domains/example">Learn more</a></p>
            </div></body></html>
            """;

        var digest = Reduce(html, url: new Uri("https://example.com/"));

        Assert.Equal("Example Domain", digest.Title);
        Assert.Equal([new HeadingNode(1, "Example Domain")], digest.Outline);
        Assert.Contains("documentation examples without needing permission", digest.Text);
        Assert.Equal("https://iana.org/domains/example", Assert.Single(digest.Links).Href);
    }

    // Junk "words" (long tokens, base64, data URIs) are dropped from the text.
    [Fact]
    public void Long_tokens_and_data_uris_are_dropped_from_text()
    {
        var token = new string('Q', 300);
        var html = $"<body><main><p>Readable text before {token} and after the token.</p><p>Inline image data:image/png;base64,{new string('A', 80)}== done.</p></main></body>";

        var digest = Reduce(html);

        Assert.Contains("Readable text before and after the token.", digest.Text);
        Assert.DoesNotContain("base64", digest.Text);
        Assert.DoesNotMatch("Q{50}", digest.Text);
    }
}
