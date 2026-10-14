using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Extraction.Design;

namespace ProjectMD.Api.Tests.Extraction;

// Tests for the pure part of design capture: colours, fonts, radii, shadows, spacing, sample
// parsing, the utility-framework check and the size cap. No browser is involved.
public class DesignAnalyzerTests
{
    private static DesignSample Sample(
        IEnumerable<ColorSample>? colors = null,
        IEnumerable<FontSample>? fonts = null,
        IEnumerable<ValueSample>? radii = null,
        IEnumerable<ValueSample>? shadows = null,
        IEnumerable<ValueSample>? spacing = null,
        IEnumerable<ClassSample>? classes = null,
        int sampled = 100) =>
        new(sampled, [.. colors ?? []], [.. fonts ?? []], [.. radii ?? []], [.. shadows ?? []], [.. spacing ?? []], [.. classes ?? []]);

    // Similar shades merge into the most used original; transparent colours are dropped.
    [Fact]
    public void Colors_merge_close_shades_and_ignore_transparent()
    {
        var colors = DesignAnalyzer.Colors(
        [
            new ColorSample("text", "rgb(17, 24, 39)", 50),
            new ColorSample("text", "rgb(16, 25, 39)", 10),
            new ColorSample("background", "rgba(0, 0, 0, 0)", 99),
            new ColorSample("background", "rgb(255, 255, 255)", 40),
        ]);

        Assert.Equal(2, colors.Count);
        Assert.Equal(new ColorToken("#111827", 60, "text"), colors[0]);
        Assert.Equal(new ColorToken("#ffffff", 40, "background"), colors[1]);
    }

    // Roles: most used text and background, a vivid colour on interactive elements is the accent,
    // a border colour stays "border".
    [Fact]
    public void Colors_guess_roles()
    {
        var colors = DesignAnalyzer.Colors(
        [
            new ColorSample("text", "#222222", 80),
            new ColorSample("background", "#ffffff", 70),
            new ColorSample("background", "#3b82f6", 6),
            new ColorSample("accent", "#3b82f6", 6),
            new ColorSample("border", "#e5e7eb", 20),
            new ColorSample("background", "#f3f4f6", 5),
            new ColorSample("accent", "#f3f4f6", 1),
        ]);

        string RoleOf(string hex) => colors.Single(c => c.Hex == hex).Role;

        Assert.Equal("text", RoleOf("#222222"));
        Assert.Equal("background", RoleOf("#ffffff"));
        Assert.Equal("accent", RoleOf("#3b82f6"));
        Assert.Equal("border", RoleOf("#e5e7eb"));
        Assert.Equal("background", RoleOf("#f3f4f6"));
    }

    // Only the 12 most used colours remain, ordered by use then hex; same input twice gives the same list.
    [Fact]
    public void Colors_keep_top_twelve_in_a_stable_order()
    {
        var samples = Enumerable.Range(0, 20)
            .Select(i => new ColorSample("text", $"rgb({i * 12}, 0, 0)", 100 - i))
            .Reverse()
            .ToList();

        var first = DesignAnalyzer.Colors(samples);
        var second = DesignAnalyzer.Colors([.. samples.AsEnumerable().Reverse()]);

        Assert.Equal(DesignAnalyzer.MaxColors, first.Count);
        Assert.Equal(first, second);
        Assert.Equal(first.OrderByDescending(c => c.Uses).ThenBy(c => c.Hex, StringComparer.Ordinal), first);
        Assert.Equal("#000000", first[0].Hex);
    }

    // Unknown roles and non-positive counts are ignored.
    [Fact]
    public void Colors_ignore_bad_rows()
    {
        var colors = DesignAnalyzer.Colors([new ColorSample("weird", "#fff", 5), new ColorSample("text", "#fff", 0), new ColorSample("text", "nope", 4)]);

        Assert.Empty(colors);
    }

    // Font family cleaning: quotes stripped, generic and system aliases skipped.
    [Theory]
    [InlineData("Inter, sans-serif", "Inter")]
    [InlineData("\"Helvetica Neue\", Arial, sans-serif", "Helvetica Neue")]
    [InlineData("'Open Sans',sans-serif", "Open Sans")]
    [InlineData("-apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto", "Segoe UI")]
    [InlineData("system-ui, sans-serif", null)]
    [InlineData("\"A, B\", serif", "A, B")]
    [InlineData("monospace", null)]
    [InlineData("", null)]
    public void CleanFontFamily_returns_first_real_family(string input, string? expected)
    {
        Assert.Equal(expected, DesignAnalyzer.CleanFontFamily(input));
    }

    // Fonts: grouped by cleaned family, top 4 by use, sizes rounded and sorted ascending (top 6),
    // weights distinct and ascending.
    [Fact]
    public void Fonts_group_by_family_and_round_sizes()
    {
        var fonts = DesignAnalyzer.Fonts(
        [
            new FontSample("Inter, sans-serif", "400", "16px", 50),
            new FontSample("\"Inter\", system-ui", "700", "15.6px", 10),
            new FontSample("Inter", "400", "14px", 20),
            new FontSample("Georgia, serif", "bold", "20px", 5),
            new FontSample("sans-serif", "400", "12px", 99),
            new FontSample("Inter", "400", "calc(1rem)", 99),
        ]);

        Assert.Equal(2, fonts.Count);
        Assert.Equal("Inter", fonts[0].Family);
        Assert.Equal([400, 700], fonts[0].Weights);
        Assert.Equal(["14px", "16px"], fonts[0].Sizes);
        Assert.Equal("Georgia", fonts[1].Family);
        Assert.Equal([700], fonts[1].Weights);
    }

    // Only four families and six sizes per family are kept.
    [Fact]
    public void Fonts_are_limited()
    {
        var samples = Enumerable.Range(0, 6).Select(i => new FontSample($"Font{i}", "400", $"{10 + i}px", 100 - i)).ToList();
        samples.AddRange(Enumerable.Range(0, 9).Select(i => new FontSample("Font0", "400", $"{30 + i}px", 1)));

        var fonts = DesignAnalyzer.Fonts(samples);

        Assert.Equal(DesignAnalyzer.MaxFonts, fonts.Count);
        Assert.Equal(DesignAnalyzer.MaxSizesPerFont, fonts[0].Sizes.Count);
        Assert.Equal(fonts[0].Sizes.OrderBy(s => int.Parse(s[..^2])), fonts[0].Sizes);
    }

    // Spacing: pixel values 1-128 only, the 8 most frequent, sorted ascending.
    [Fact]
    public void Spacing_keeps_the_eight_most_frequent_sorted()
    {
        var samples = new[] { 4, 8, 12, 16, 24, 32, 48, 64, 96, 2 }
            .Select((px, i) => new ValueSample(px.ToString(), 100 - i))
            .Concat([new ValueSample("0", 500), new ValueSample("300", 500), new ValueSample("x", 500), new ValueSample("8", 3)])
            .ToList();

        var spacing = DesignAnalyzer.Spacing(samples);

        Assert.Equal(["4px", "8px", "12px", "16px", "24px", "32px", "48px", "64px"], spacing);
    }

    // Radii and shadows: equal values merge, 0px is ignored, shadows are shortened and limited.
    [Fact]
    public void Radii_and_shadows_are_limited_and_cleaned()
    {
        var long80 = "rgba(0, 0, 0, 0.1) 0px 4px 6px -1px, " + new string('z', 100);
        var design = DesignAnalyzer.Build(
            Sample(
                radii: [new ValueSample("0px", 99), new ValueSample("8px", 5), new ValueSample("8px", 5), new ValueSample("4px", 9), new ValueSample("9999px", 1), new ValueSample("12px", 1), new ValueSample("2px", 1), new ValueSample("3px", 1)],
                shadows: [new ValueSample("none", 1), new ValueSample(long80, 3), new ValueSample("a  b", 2)]),
            [])!;

        Assert.Equal(["8px", "4px"], design.Radii.Take(2));
        Assert.Equal(DesignAnalyzer.MaxRadii, design.Radii.Count);
        Assert.DoesNotContain("0px", design.Radii);
        Assert.All(design.Shadows, s => Assert.True(s.Length <= DesignAnalyzer.MaxShadowChars));
        Assert.Contains("a b", design.Shadows);
    }

    // The same sample always produces an identical summary.
    [Fact]
    public void Build_is_deterministic()
    {
        var sample = Sample(
            colors: [new ColorSample("text", "#111", 5), new ColorSample("background", "#fff", 5)],
            fonts: [new FontSample("Inter", "400", "16px", 3)],
            spacing: [new ValueSample("8", 2), new ValueSample("16", 2)]);
        var sheets = new[] { ":root{--color-a:#111;--space:8px}@media (min-width:640px){a{}}" };

        Assert.Equal(DesignAnalyzer.Build(sample, sheets)!.ToString(), DesignAnalyzer.Build(sample, sheets)!.ToString());
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(DesignAnalyzer.Build(sample, sheets)),
            System.Text.Json.JsonSerializer.Serialize(DesignAnalyzer.Build(sample, sheets)));
    }

    // An empty sample (no elements seen) gives null rather than an empty summary.
    [Fact]
    public void Build_returns_null_when_nothing_was_sampled()
    {
        Assert.Null(DesignAnalyzer.Build(Sample(sampled: 0), []));
    }

    // The summary is trimmed toward the size target by dropping extra CSS variables, never below
    // 20, and stays within the 40 variable limit.
    [Fact]
    public void Size_is_capped_by_trimming_css_variables()
    {
        var css = ":root{" + string.Concat(Enumerable.Range(0, 60).Select(i => $"--color-token-name-{i}: #aabbcc;")) + "}";
        var sample = Sample(colors: [new ColorSample("text", "#111111", 5)]);

        var design = DesignAnalyzer.Build(sample, [css])!;

        Assert.True(design.CssVariables.Count is >= DesignAnalyzer.MinVariablesKept and <= CssAnalyzer.MaxVariables);
        Assert.True(DesignAnalyzer.SerialisedBytes(design) <= DesignAnalyzer.TargetJsonBytes
            || design.CssVariables.Count == DesignAnalyzer.MinVariablesKept);
        Assert.True(design.CssVariables.Count < CssAnalyzer.MaxVariables);
    }

    // A small design is not trimmed.
    [Fact]
    public void Small_summary_is_not_trimmed()
    {
        var design = DesignAnalyzer.Build(Sample(colors: [new ColorSample("text", "#111111", 5)]), [":root{--a:1;--b:2}"])!;

        Assert.Equal(2, design.CssVariables.Count);
        Assert.True(DesignAnalyzer.SerialisedBytes(design) < DesignAnalyzer.TargetJsonBytes);
    }

    // The script answer is read defensively: wrong shapes are skipped, junk is rejected.
    [Fact]
    public void Sample_parsing_skips_bad_rows_and_rejects_junk()
    {
        const string json = """
            {"sampled":12,"colors":[["text","rgb(1, 2, 3)",4],["bad"],"x",["background",5,"y"]],
             "fonts":[["Inter","400","16px",2]],"radii":[["8px",3]],"shadows":[],"spacing":[["8",2]],
             "classes":[["flex px-4 text-sm",5]]}
            """;

        Assert.True(DesignSample.TryParse(json, out var sample));
        Assert.Equal(12, sample.Sampled);
        Assert.Equal(2, sample.Colors.Count);
        Assert.Equal(new ColorSample("text", "rgb(1, 2, 3)", 4), sample.Colors[0]);
        Assert.Equal(["flex", "px-4", "text-sm"], sample.Classes[0].Classes);

        Assert.False(DesignSample.TryParse("not json", out _));
        Assert.False(DesignSample.TryParse("[1,2]", out _));
        Assert.False(DesignSample.TryParse("", out _));
        Assert.False(DesignSample.TryParse(null, out _));
        Assert.True(DesignSample.TryParse("{}", out var empty));
        Assert.Equal(0, empty.Sampled);
    }

    // Tailwind-style class lists on most elements are detected; plain or semantic classes are not.
    [Fact]
    public void Utility_framework_detection()
    {
        var tailwind = new[]
        {
            new ClassSample(["flex", "items-center", "px-4", "text-sm", "md:grid"], 30),
            new ClassSample(["bg-white", "rounded-lg", "shadow-md", "p-6"], 20),
        };
        var semantic = new[] { new ClassSample(["nav", "nav-item", "active"], 80), new ClassSample(["card", "card-body", "mb-3"], 5) };
        var bootstrapish = new[] { new ClassSample(["d-flex", "mt-3", "text-center"], 90) };

        Assert.Equal("Tailwind", UtilityFrameworkDetector.Detect(100, tailwind));
        Assert.Null(UtilityFrameworkDetector.Detect(100, semantic));
        Assert.Null(UtilityFrameworkDetector.Detect(100, bootstrapish));
        Assert.Null(UtilityFrameworkDetector.Detect(200, tailwind));
        Assert.Null(UtilityFrameworkDetector.Detect(0, tailwind));
        Assert.Null(UtilityFrameworkDetector.Detect(100, []));
    }

    // Just over and under the 40% line.
    [Fact]
    public void Utility_framework_threshold_is_forty_percent()
    {
        ClassSample Tw(int n) => new(["flex", "px-4", "text-sm"], n);

        Assert.Null(UtilityFrameworkDetector.Detect(100, [Tw(40)]));
        Assert.Equal("Tailwind", UtilityFrameworkDetector.Detect(100, [Tw(41)]));
    }
}
