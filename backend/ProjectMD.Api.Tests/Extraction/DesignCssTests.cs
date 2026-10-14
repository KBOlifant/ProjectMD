using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Extraction.Design;

namespace ProjectMD.Api.Tests.Extraction;

// Tests for reading CSS variables and breakpoints out of stylesheet text.
public class DesignCssTests
{
    private static IReadOnlyList<string> Sheet(string css) => [css];

    // Variables are read from :root and html; comments, !important and var() values are handled.
    [Fact]
    public void Variables_are_read_from_root_with_comments_important_and_var_values()
    {
        const string css = """
            /* header comment { --fake: 1; } */
            :root {
              --color-primary: #3b82f6 !important;
              /* --hidden: red; */
              --space-4:   1rem;
              --shadow: 0 1px 2px rgba(0,0,0,.2);
              --alias: var(--color-primary, #000);
            }
            html { --radius: 8px }
            .card { --not-root: 1; }
            """;

        var vars = CssAnalyzer.ExtractVariables(Sheet(css));

        Assert.Contains(new CssVariable("--color-primary", "#3b82f6"), vars);
        Assert.Contains(new CssVariable("--space-4", "1rem"), vars);
        Assert.Contains(new CssVariable("--shadow", "0 1px 2px rgba(0,0,0,.2)"), vars);
        Assert.Contains(new CssVariable("--alias", "var(--color-primary, #000)"), vars);
        Assert.Contains(new CssVariable("--radius", "8px"), vars);
        Assert.DoesNotContain(vars, v => v.Name is "--fake" or "--hidden" or "--not-root");
    }

    // Variables only declared inside @media (theme overrides) are skipped, but :root inside
    // @layer counts, and a later top-level declaration wins over an earlier one.
    [Fact]
    public void Variables_inside_media_are_skipped_and_layers_are_looked_through()
    {
        const string css = """
            @charset "utf-8";
            @import url("other.css");
            :root { --bg: #fff; }
            @media (prefers-color-scheme: dark) { :root { --bg: #000; --dark-only: #111; } }
            @layer base { :root { --fg: #222; } }
            :root { --bg: #fafafa; }
            """;

        var vars = CssAnalyzer.ExtractVariables(Sheet(css));

        Assert.Equal("#fafafa", vars.Single(v => v.Name == "--bg").Value);
        Assert.Equal("#222", vars.Single(v => v.Name == "--fg").Value);
        Assert.DoesNotContain(vars, v => v.Name == "--dark-only");
    }

    // A theme selector is not the default root.
    [Fact]
    public void Theme_selectors_are_not_root()
    {
        var vars = CssAnalyzer.ExtractVariables(Sheet(".dark, [data-theme=dark] { --bg: #000; } :root, :host { --ok: 1; }"));

        Assert.DoesNotContain(vars, v => v.Name == "--bg");
        Assert.Contains(vars, v => v.Name == "--ok");
    }

    // Long values are cut at 60 characters; at most 40 variables are kept; design-related names
    // come first; order is stable.
    [Fact]
    public void Variables_are_capped_cut_and_prefer_design_names()
    {
        var css = ":root{" + string.Concat(Enumerable.Range(0, 60).Select(i => $"--zzz-{i}: {i};")) +
                  "--color-late: " + new string('x', 100) + ";}";

        var vars = CssAnalyzer.ExtractVariables(Sheet(css));

        Assert.Equal(CssAnalyzer.MaxVariables, vars.Count);
        Assert.Equal("--color-late", vars[0].Name);
        Assert.Equal(CssAnalyzer.MaxValueChars, vars[0].Value.Length);
        Assert.Equal("--zzz-0", vars[1].Name);
        Assert.Equal(vars, CssAnalyzer.ExtractVariables(Sheet(css)));
    }

    // Unterminated and broken CSS never throws.
    [Theory]
    [InlineData(":root { --a: 1")]
    [InlineData("}}}{{{ ; ; :root{--a:(}")]
    [InlineData("/* never closed :root{--a:1}")]
    [InlineData("")]
    public void Broken_css_does_not_throw(string css)
    {
        var vars = CssAnalyzer.ExtractVariables(Sheet(css));
        var points = CssAnalyzer.ExtractBreakpoints(Sheet(css));

        Assert.NotNull(vars);
        Assert.NotNull(points);
    }

    // Breakpoints come from min/max-width (nested, em/rem, range syntax), sorted, distinct, max 8.
    [Fact]
    public void Breakpoints_are_distinct_sorted_and_capped()
    {
        const string css = """
            @media (min-width: 768px) { .a { color: red } @media (max-width: 1023.98px) { .b {} } }
            @media screen and (min-width:768px) and (max-width: 1200px) { .c {} }
            @media (min-width: 40em) { .d {} }
            @media (width >= 1536px) { .e {} }
            @media (prefers-reduced-motion: reduce) { .f {} }
            @media print { .g {} }
            """;

        var points = CssAnalyzer.ExtractBreakpoints(Sheet(css));

        Assert.Equal(["640px", "768px", "1024px", "1200px", "1536px"], points);

        var many = string.Concat(Enumerable.Range(1, 12).Select(i => $"@media (min-width: {i * 100}px){{.x{{}}}}"));
        Assert.Equal(CssAnalyzer.MaxBreakpoints, CssAnalyzer.ExtractBreakpoints(Sheet(many)).Count);
    }

    // Media rules inside comments are ignored.
    [Fact]
    public void Breakpoints_in_comments_are_ignored()
    {
        Assert.Empty(CssAnalyzer.ExtractBreakpoints(Sheet("/* @media (min-width: 500px) { a{} } */ a{color:red}")));
    }

    // Inline <style> blocks are found in rendered HTML, capped in count.
    [Fact]
    public void Inline_styles_are_extracted_from_html()
    {
        var html = "<html><head><style>:root{--a:1}</style><STYLE media='x'>b{}</STYLE></head><body></body></html>";

        var sheets = CssAnalyzer.InlineStyles(html);

        Assert.Equal([":root{--a:1}", "b{}"], sheets);
        Assert.Empty(CssAnalyzer.InlineStyles(null));
        Assert.Equal(CssAnalyzer.MaxSheets, CssAnalyzer.InlineStyles(string.Concat(Enumerable.Repeat("<style>a{}</style>", 100))).Count);
    }

    // The stylesheet collector keeps only stylesheet bodies and cuts each to 256 KB.
    [Fact]
    public void StyleSheetTexts_keeps_only_stylesheets_and_caps_size()
    {
        var texts = new StyleSheetTexts();

        texts.Add("script", "x"u8.ToArray());
        texts.Add("stylesheet", new byte[CssAnalyzer.MaxSheetChars + 5000].Select(_ => (byte)'a').ToArray());
        texts.Add("stylesheet", []);

        var snapshot = texts.Snapshot();
        Assert.Single(snapshot);
        Assert.Equal(CssAnalyzer.MaxSheetChars, snapshot[0].Length);
    }
}
