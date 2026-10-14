using ProjectMD.Api.Models;
using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Pipeline;

namespace ProjectMD.Api.Tests.Ai;

// Tests for the AI-free document builder and the confidence heuristic.
public class DeterministicMarkdownBuilderTests
{
    private static readonly Uri Requested = new("https://acme.example/");

    private static MarkdownInput Input(PageDigest? digest = null, string? prose = null, ScanBasis basis = ScanBasis.Extract) => new(
        "acme.example",
        Requested,
        new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc),
        digest ?? Digests.Simple(),
        [new ScanTech { Name = "Next.js", Highlighted = true }, new ScanTech { Name = "Tailwind CSS", Highlighted = false }],
        72,
        1,
        prose,
        basis);

    // Every footer variant names how the document was made, and fallbacks say "Basic scan".
    [Theory]
    [InlineData(ScanBasis.Extract, DeterministicMarkdownBuilder.FooterExtract)]
    [InlineData(ScanBasis.Ai, DeterministicMarkdownBuilder.FooterAi)]
    [InlineData(ScanBasis.AiNotConfigured, DeterministicMarkdownBuilder.FooterNotConfigured)]
    [InlineData(ScanBasis.AiBudget, DeterministicMarkdownBuilder.FooterBudget)]
    [InlineData(ScanBasis.AiFailed, DeterministicMarkdownBuilder.FooterFailed)]
    public void Footer_reflects_how_the_document_was_made(ScanBasis basis, string footer)
    {
        var markdown = DeterministicMarkdownBuilder.Build(Input(basis: basis));

        Assert.EndsWith($"*{footer}*\n", markdown);
        if (basis != ScanBasis.Ai)
        {
            Assert.Contains("Basic scan", footer);
        }
    }

    // The not-configured footer says exactly that.
    [Fact]
    public void Not_configured_footer_mentions_it()
    {
        Assert.Contains("AI writing is not configured", DeterministicMarkdownBuilder.FooterNotConfigured);
    }

    // AI prose appears in its own section, only when there is some.
    [Fact]
    public void Ai_prose_gets_its_own_section()
    {
        var with = DeterministicMarkdownBuilder.Build(Input(prose: "### Summary\nAcme.", basis: ScanBasis.Ai));
        var without = DeterministicMarkdownBuilder.Build(Input());

        Assert.Contains("## About this site\n\n### Summary\nAcme.\n", with);
        Assert.DoesNotContain("About this site", without);
    }

    // Page text with Markdown or HTML characters is escaped so it cannot inject formatting or markup.
    [Fact]
    public void Page_text_is_escaped()
    {
        var digest = Digests.Simple(title: "Evil *bold* [link](javascript:alert(1)) <script>x</script> # not a heading") with
        {
            MetaDescription = "line one\nline two | pipe",
            Links = [new LinkRef("a](http://evil) b", "https://acme.example/a b(c)")],
            Outline = [new HeadingNode(1, "<img src=x onerror=y>")],
        };

        var markdown = DeterministicMarkdownBuilder.Build(Input(digest));

        // Every markup character is backslash-escaped, so nothing renders as HTML, a link or emphasis.
        Assert.DoesNotContain("<script>", markdown);
        Assert.Contains("\\<script\\>x\\</script\\>", markdown);
        Assert.Contains("\\<img src=x onerror=y\\>", markdown);
        Assert.Contains("Evil \\*bold\\* \\[link\\]", markdown);
        Assert.Contains("\\# not a heading", markdown);
        Assert.Contains("line one line two", markdown);

        // The link text cannot close the link early, and the address is percent-encoded.
        Assert.Contains("- [a\\](http://evil) b](https://acme.example/a%20b%28c%29)", markdown);
    }

    // Non-http links are not listed.
    [Fact]
    public void Only_http_links_are_listed()
    {
        var digest = Digests.Simple() with
        {
            Links = [new LinkRef("Bad", "javascript:alert(1)"), new LinkRef("Mail", "mailto:a@b.c"), new LinkRef("Good", "https://acme.example/good")],
        };

        var markdown = DeterministicMarkdownBuilder.Build(Input(digest));

        Assert.DoesNotContain("javascript:", markdown);
        Assert.DoesNotContain("mailto:", markdown);
        Assert.Contains("[Good](https://acme.example/good)", markdown);
    }

    // A page with nothing on it still gives a valid document with placeholders.
    [Fact]
    public void Empty_page_gives_placeholders()
    {
        var digest = Digests.Simple() with { Title = null, MetaDescription = null, Language = null, Outline = [], Links = [], Text = string.Empty, JsonLd = [] };
        var input = Input(digest) with { Tech = [] };

        var markdown = DeterministicMarkdownBuilder.Build(input);

        Assert.Contains("_No technologies were recognised from the page._", markdown);
        Assert.Contains("_No headings were found on the page._", markdown);
        Assert.DoesNotContain("## Key Links", markdown);
    }

    // The document stays far below the 1 MB storage limit even for a huge page.
    [Fact]
    public void Document_stays_small_for_a_huge_page()
    {
        var markdown = DeterministicMarkdownBuilder.Build(Input(Digests.Huge()));

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(markdown) < 50_000);
    }

    // A redirect shows the final address too.
    [Fact]
    public void Redirects_show_the_final_url()
    {
        var digest = Digests.Simple() with { FinalUrl = new Uri("https://www.acme.example/home") };

        var markdown = DeterministicMarkdownBuilder.Build(Input(digest));

        Assert.Contains("- **Final URL:** <https://www.acme.example/home>", markdown);
    }

    // The confidence heuristic is deterministic, in range and rises with more signals.
    [Fact]
    public void Confidence_heuristic_rises_with_signals_and_stays_in_range()
    {
        var empty = Digests.Simple() with { StatusCode = 500, Title = null, MetaDescription = null, Outline = [], Text = string.Empty, JsonLd = [] };
        var rich = Digests.Simple(text: new string('x', 2500));
        var tech = new[] { new ScanTech { Name = "a" }, new ScanTech { Name = "b" }, new ScanTech { Name = "c" }, new ScanTech { Name = "d" } };

        var low = ConfidenceEstimator.Estimate(empty, []);
        var high = ConfidenceEstimator.Estimate(rich, tech);

        Assert.Equal(0, low);
        Assert.Equal(100, high);
        Assert.Equal(high, ConfidenceEstimator.Estimate(rich, tech));
    }
}
