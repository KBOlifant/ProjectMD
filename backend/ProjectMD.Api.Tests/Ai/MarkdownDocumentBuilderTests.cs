using ProjectMD.Api.Models;
using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Pipeline;

namespace ProjectMD.Api.Tests.Ai;

// Tests for the one document renderer (with and without AI notes), the escaping of hostile text
// and the confidence heuristic.
public class MarkdownDocumentBuilderTests
{
    private static readonly Uri Requested = new("https://acme.example/");

    private static ScanFacts Facts(PageDigest? digest = null, ScanBasis basis = ScanBasis.Extract) => new(
        "acme.example",
        Requested,
        new DateTime(2026, 10, 14, 8, 30, 0, DateTimeKind.Utc),
        digest ?? Digests.Simple(),
        [new ScanTech { Name = "Next.js", Highlighted = true }, new ScanTech { Name = "Vercel", Highlighted = true }, new ScanTech { Name = "WordPress 6.4" }],
        72,
        3200,
        basis);

    private static AiDocument Notes(PageDigest digest) =>
        AiDocumentParser.Parse(AiJson.Full("Acme Cloud hosts apps.", "E1", "E2"), EndpointRanking.KnownIds(digest.Api))!;

    // The sections appear in the fixed order, with AI notes.
    [Fact]
    public void Full_document_has_every_section_in_order()
    {
        var digest = AiJson.Rich();

        var md = MarkdownDocumentBuilder.Build(Facts(digest, ScanBasis.Ai), Notes(digest));

        var headings = new[]
        {
            "## Overview", "## Tech stack", "## Architecture", "## API endpoints", "### Response shapes",
            "### Third-party services", "## Design system", "### Colors", "### Typography",
            "### Radii, shadows, spacing", "### CSS variables", "### Breakpoints", "### Notes",
            "## Page structure", "## Developer tips",
        };
        var positions = headings.Select(h => md.IndexOf("\n" + h + "\n", StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p), positions);
        Assert.StartsWith("---\ntitle: \"Acme Cloud\"\nurl: \"https://acme.example/\"\nscanned_at: 2026-10-14T08:30:00Z\nscanner: ai\n", md);
        Assert.Contains("technologies:\n  - \"Next.js\"\n", md);
        Assert.Contains("\n# acme.example\n", md);
        Assert.Contains("*Status 200 · scanned in 3.2 s · confidence 72% (a heuristic estimate", md);
        Assert.Contains("Acme Cloud hosts apps.", md);
        Assert.EndsWith($"*{MarkdownDocumentBuilder.FooterAi}*\n", md);
        Assert.DoesNotContain("\r", md);
    }

    // The tech table has version and role columns; the rendering line is labelled as a guess.
    [Fact]
    public void Tech_table_has_version_role_and_rendering_line()
    {
        var md = MarkdownDocumentBuilder.Build(Facts(AiJson.Rich()), null);

        Assert.Contains("| Technology | Version | Role |\n| --- | --- | --- |\n", md);
        Assert.Contains("| Next.js | - | Framework |", md);
        Assert.Contains("| Vercel | - | Hosting |", md);
        Assert.Contains("| WordPress | 6.4 | Platform / CMS |", md);
        Assert.Contains("**Rendering:** Next.js page data is embedded", md);
        Assert.Contains("(guessed from page signals)", md);
    }

    // The API table keeps the ranking order, shows ids' purposes for the first 15 and "-" otherwise.
    [Fact]
    public void Api_table_uses_ids_for_purposes()
    {
        var digest = AiJson.Rich();

        var md = MarkdownDocumentBuilder.Build(Facts(digest, ScanBasis.Ai), Notes(digest));

        Assert.Contains("| Method | Path | Status | Type | Purpose |", md);
        Assert.Contains("| GET | `/api/users/{id}` params: expand | 200, 404 | application/json | Purpose of E1 |", md);
        Assert.Contains("| POST | `/graphql` (query GetUser) | 200 | application/json | Purpose of E2 |", md);
        Assert.Contains("| GET | `api.acme.example/v1/items` | 200 | application/json | - |", md);
        Assert.Contains("**GET** `/api/users/{id}`\n\n```json\n{id:number,name:string}\n```", md);
        Assert.Contains("- **analytics:** `www.google-analytics.com` (4)\n- **other:** `cdn.unknown.example` (1)", md);
        Assert.Contains("| `#3b82f6` | accent | 40 |", md);
        Assert.Contains("```css\n:root {\n  --color-primary: #3b82f6;\n  --radius: 8px;\n}\n```", md);
        Assert.Contains("- **Utility framework:** Tailwind", md);
    }

    // A basic scan renders the same sections with blank explanations and the basic footer.
    [Fact]
    public void Basic_scan_has_blank_explanations_and_basic_footer()
    {
        var md = MarkdownDocumentBuilder.Build(Facts(AiJson.Rich(), ScanBasis.AiFailed), null);

        Assert.Contains("scanner: basic", md);
        Assert.Contains("| GET | `/api/users/{id}` params: expand | 200, 404 | application/json | - |", md);
        Assert.Contains("Acme Cloud runs your apps close to your users.", md);
        Assert.DoesNotContain("## Architecture", md);
        Assert.DoesNotContain("## Developer tips", md);
        Assert.DoesNotContain("### Notes", md);
        Assert.EndsWith($"*{MarkdownDocumentBuilder.FooterFailed}*\n", md);
    }

    // Sections for data that was not captured are left out (API only, design only, neither).
    [Fact]
    public void Missing_capture_data_leaves_those_sections_out()
    {
        var rich = AiJson.Rich();

        var apiOnly = MarkdownDocumentBuilder.Build(Facts(rich with { Design = null }), null);
        var designOnly = MarkdownDocumentBuilder.Build(Facts(rich with { Api = null }), null);
        var neither = MarkdownDocumentBuilder.Build(Facts(rich with { Api = null, Design = null }), null);

        Assert.Contains("## API endpoints", apiOnly);
        Assert.DoesNotContain("## Design system", apiOnly);
        Assert.DoesNotContain("## API endpoints", designOnly);
        Assert.Contains("## Design system", designOnly);
        Assert.DoesNotContain("## API endpoints", neither);
        Assert.DoesNotContain("## Design system", neither);
        Assert.Contains("## Page structure", neither);
    }

    // AI notes about endpoints or design are ignored when that data was not captured.
    [Fact]
    public void Ai_design_notes_without_design_data_are_not_rendered()
    {
        var digest = AiJson.Rich() with { Api = null, Design = null };
        var notes = AiDocumentParser.Parse(AiJson.Full("x"), new HashSet<string>())!;

        var md = MarkdownDocumentBuilder.Build(Facts(digest, ScanBasis.Ai), notes);

        Assert.DoesNotContain("Dark theme with a blue accent", md);
        Assert.Contains("## Architecture", md);
    }

    // The API list says so when it was truncated.
    [Fact]
    public void Truncated_api_list_is_noted()
    {
        var rich = AiJson.Rich();
        var digest = rich with { Api = rich.Api! with { Truncated = true } };

        Assert.Contains("truncated", MarkdownDocumentBuilder.Build(Facts(digest), null));
    }

    // Same facts and same notes: byte-identical Markdown.
    [Fact]
    public void Output_is_deterministic()
    {
        var digest = AiJson.Rich();

        var a = MarkdownDocumentBuilder.Build(Facts(digest, ScanBasis.Ai), Notes(digest));
        var b = MarkdownDocumentBuilder.Build(Facts(AiJson.Rich(), ScanBasis.Ai), Notes(AiJson.Rich()));

        Assert.Equal(a, b);
    }

    // Hostile strings in every field of the facts and notes cannot add structure to the document.
    [Fact]
    public void Hostile_strings_in_every_field_stay_inert()
    {
        var h = AiDocumentTests.Hostile;
        var digest = Digests.Simple(title: h) with
        {
            MetaDescription = h,
            Outline = [new HeadingNode(1, h), new HeadingNode(2, h)],
            Api = new ApiCatalog(
                [new ApiEndpoint(h, "evil.example", h, true, 1, [200], h, "{a:string}```\n# Heading\n```", h, [h])],
                [new ThirdPartyHost(h, 1, h)],
                false),
            Design = new DesignSystem(
                [new ColorToken(h, 1, h)],
                [new FontToken(h, [400], [h])],
                [h],
                [h],
                [h],
                [new CssVariable("--x", h + "\n}\n```\n# Heading"), new CssVariable(h, "1")],
                [h],
                h),
        };
        var notes = AiDocumentParser.Parse(AiJson.Full(h, "E1"), EndpointRanking.KnownIds(digest.Api))!;
        var evilTech = new[] { new ScanTech { Name = h } };

        var md = MarkdownDocumentBuilder.Build(Facts(digest, ScanBasis.Ai) with { Tech = evilTech }, notes);

        var (outside, unclosed) = OutsideFences(md);
        Assert.False(unclosed, "a code fence was never closed");
        // A backslash-escaped angle bracket is inert text, so only unescaped ones count.
        var text = string.Join("\n", outside).Replace("\\<", string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("<", text);
        Assert.DoesNotContain("<script", text);
        Assert.DoesNotContain("<img", text);
        // Link syntax is only ever inside an inline code span (inert) or escaped.
        var withoutCode = System.Text.RegularExpressions.Regex.Replace(text, "(?<!`)(`+)(?!`).*?(?<!`)\\1(?!`)", string.Empty);
        Assert.DoesNotContain("](", withoutCode.Replace("\\](", string.Empty, StringComparison.Ordinal));

        // The only heading lines outside code blocks are the fixed ones.
        var fixedHeadings = new[]
        {
            "## Overview", "## Tech stack", "## Architecture", "## API endpoints", "### Response shapes",
            "### Third-party services", "## Design system", "### Colors", "### Typography", "### Radii, shadows, spacing",
            "### CSS variables", "### Breakpoints", "### Notes", "## Page structure", "## Developer tips",
        };
        Assert.All(outside.Where(l => l.StartsWith('#')), line => Assert.True(
            line == "# acme.example" || fixedHeadings.Contains(line), line));

        // Every table row has the expected number of unescaped separators (3 to 6 columns).
        foreach (var row in outside.Where(l => l.StartsWith("| ", StringComparison.Ordinal)))
        {
            Assert.InRange(CountUnescapedPipes(row), 4, 6);
        }
    }

    // The lines of the document that are not inside a fenced code block, and whether a fence was
    // left open. A fence closes only on a line of at least as many backticks and nothing else.
    private static (List<string> Outside, bool Unclosed) OutsideFences(string md)
    {
        var outside = new List<string>();
        var open = 0;
        foreach (var line in md.Split('\n'))
        {
            var ticks = line.TakeWhile(c => c == '`').Count();
            if (open == 0)
            {
                if (ticks >= 3)
                {
                    open = ticks;
                }
                else
                {
                    outside.Add(line);
                }
            }
            else if (ticks >= open && line.Trim('`').Length == 0)
            {
                open = 0;
            }
        }

        return (outside, open != 0);
    }
    // Counts the separators of a table row, ignoring pipes inside code spans (a span opens with a run
    // of backticks and closes with a run of the same length) or escaped with a backslash.
    private static int CountUnescapedPipes(string row)
    {
        var count = 0;
        var openRun = 0;
        for (var i = 0; i < row.Length; i++)
        {
            if (row[i] == '\\')
            {
                i++;
            }
            else if (row[i] == '`')
            {
                var run = 0;
                while (i + run < row.Length && row[i + run] == '`')
                {
                    run++;
                }

                if (openRun == 0)
                {
                    openRun = run;
                }
                else if (run == openRun)
                {
                    openRun = 0;
                }

                i += run - 1;
            }
            else if (row[i] == '|' && openRun == 0)
            {
                count++;
            }
        }

        return count;
    }
    // Every footer variant names how the document was made, and fallbacks say "Basic scan".
    [Theory]
    [InlineData(ScanBasis.Extract, MarkdownDocumentBuilder.FooterExtract)]
    [InlineData(ScanBasis.Ai, MarkdownDocumentBuilder.FooterAi)]
    [InlineData(ScanBasis.AiNotConfigured, MarkdownDocumentBuilder.FooterNotConfigured)]
    [InlineData(ScanBasis.AiBudget, MarkdownDocumentBuilder.FooterBudget)]
    [InlineData(ScanBasis.AiFailed, MarkdownDocumentBuilder.FooterFailed)]
    public void Footer_reflects_how_the_document_was_made(ScanBasis basis, string footer)
    {
        var md = MarkdownDocumentBuilder.Build(Facts(basis: basis), null);

        Assert.EndsWith($"*{footer}*\n", md);
        Assert.Contains("Confidence and other estimates are heuristics", md);
        if (basis != ScanBasis.Ai)
        {
            Assert.Contains("Basic scan", footer);
        }
    }

    // A page with nothing on it still gives a valid document with placeholders.
    [Fact]
    public void Empty_page_gives_placeholders()
    {
        var digest = Digests.Simple() with { Title = null, MetaDescription = null, Language = null, Outline = [], Links = [], Text = string.Empty, JsonLd = [] };

        var md = MarkdownDocumentBuilder.Build(Facts(digest) with { Tech = [] }, null);

        Assert.Contains("title: \"acme.example\"", md);
        Assert.Contains("technologies: []", md);
        Assert.Contains("_No technologies were recognised from the page._", md);
        Assert.Contains("_No headings were found on the page._", md);
        Assert.Contains("## Overview\n\n-\n", md);
    }

    // Long pages stay small: at most 40 outline items and 12 technologies.
    [Fact]
    public void Document_stays_small_for_a_huge_page()
    {
        var md = MarkdownDocumentBuilder.Build(Facts(Digests.Huge()), null);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(md) < 50_000);
        Assert.Equal(40, md.Split('\n').Count(l => l.TrimStart().StartsWith("- Heading number", StringComparison.Ordinal)));
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
