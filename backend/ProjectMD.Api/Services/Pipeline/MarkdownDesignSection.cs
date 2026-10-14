using System.Text;
using System.Text.RegularExpressions;
using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Services.Pipeline;

// Writes the "Design system" part of the scan document from the captured DesignSystem: colours,
// typography, radii/shadows/spacing, CSS variables, breakpoints and the utility framework. The AI
// only adds short notes at the end.
internal static partial class MarkdownDesignSection
{
    private const int MaxColors = 12;
    private const int MaxFonts = 4;
    private const int MaxCssVariables = 40;
    private const int MaxBreakpoints = 8;

    // A well-formed colour value; anything else is shown escaped instead of trusted.
    [GeneratedRegex(@"^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexRegex();

    // A CSS custom property name such as --color-primary.
    [GeneratedRegex(@"^--[A-Za-z0-9_-]{1,60}$", RegexOptions.CultureInvariant)]
    private static partial Regex VariableNameRegex();

    // Appends the section, or nothing when no design was captured.
    public static void Append(StringBuilder sb, DesignSystem? design, AiDocument? ai)
    {
        if (design is null)
        {
            return;
        }

        var notes = ai?.Design ?? [];
        var hasFacts = design.Colors.Count > 0 || design.Fonts.Count > 0 || design.Radii.Count > 0
            || design.Shadows.Count > 0 || design.SpacingScale.Count > 0 || design.CssVariables.Count > 0
            || design.Breakpoints.Count > 0 || !string.IsNullOrWhiteSpace(design.UtilityFramework);
        if (!hasFacts && notes.Count == 0)
        {
            return;
        }

        sb.AppendLine("## Design system").AppendLine();
        AppendColors(sb, design.Colors);
        AppendFonts(sb, design.Fonts);
        AppendScales(sb, design);
        AppendVariables(sb, design.CssVariables);
        AppendBreakpoints(sb, design);

        if (notes.Count > 0)
        {
            sb.AppendLine("### Notes").AppendLine();
            MarkdownDocumentBuilder.AppendBullets(sb, notes);
        }
    }

    // Hex | Role | Uses. The hex goes in a code span (Markdown cannot colour a swatch).
    private static void AppendColors(StringBuilder sb, IReadOnlyList<ColorToken> colors)
    {
        if (colors.Count == 0)
        {
            return;
        }

        sb.AppendLine("### Colors").AppendLine();
        sb.AppendLine("| Hex | Role | Uses |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var c in colors.Take(MaxColors))
        {
            var hex = HexRegex().IsMatch(c.Hex) ? c.Hex.ToLowerInvariant() : c.Hex;
            sb.Append("| ").Append(MarkdownText.Code(hex, 20, inTable: true))
                .Append(" | ").Append(MarkdownText.Inline(c.Role, 20))
                .Append(" | ").Append(MarkdownText.Number(c.Uses))
                .AppendLine(" |");
        }

        sb.AppendLine();
    }

    // Font | Weights | Sizes.
    private static void AppendFonts(StringBuilder sb, IReadOnlyList<FontToken> fonts)
    {
        if (fonts.Count == 0)
        {
            return;
        }

        sb.AppendLine("### Typography").AppendLine();
        sb.AppendLine("| Font | Weights | Sizes |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var f in fonts.Take(MaxFonts))
        {
            sb.Append("| ").Append(MarkdownText.Inline(f.Family, 60))
                .Append(" | ").Append(f.Weights.Count == 0 ? MarkdownDocumentBuilder.Blank : string.Join(", ", f.Weights.Select(MarkdownText.Number)))
                .Append(" | ").Append(f.Sizes.Count == 0 ? MarkdownDocumentBuilder.Blank : string.Join(", ", f.Sizes.Select(s => MarkdownText.Inline(s, 12))))
                .AppendLine(" |");
        }

        sb.AppendLine();
    }

    // Bullets for radii, shadows, spacing steps and the utility framework.
    private static void AppendScales(StringBuilder sb, DesignSystem design)
    {
        var lines = new List<string>();
        AddList(lines, "Radii", design.Radii, 5);
        AddList(lines, "Shadows", design.Shadows, 4);
        AddList(lines, "Spacing", design.SpacingScale, 8);
        if (!string.IsNullOrWhiteSpace(design.UtilityFramework))
        {
            lines.Add("- **Utility framework:** " + MarkdownText.Inline(design.UtilityFramework, 40));
        }

        if (lines.Count == 0)
        {
            return;
        }

        sb.AppendLine("### Radii, shadows, spacing").AppendLine();
        foreach (var line in lines)
        {
            sb.AppendLine(line);
        }

        sb.AppendLine();
    }

    // Adds "- **Label:** `a`, `b`" when the list has values.
    private static void AddList(List<string> lines, string label, IReadOnlyList<string> values, int max)
    {
        if (values.Count > 0)
        {
            lines.Add($"- **{label}:** " + string.Join(", ", values.Take(max).Select(v => MarkdownText.Code(v, 80))));
        }
    }

    // The root custom properties as a css block (valid names only, values flattened to one line).
    private static void AppendVariables(StringBuilder sb, IReadOnlyList<CssVariable> variables)
    {
        var valid = variables.Where(v => VariableNameRegex().IsMatch(v.Name)).Take(MaxCssVariables).ToList();
        if (valid.Count == 0)
        {
            return;
        }

        var css = new StringBuilder(":root {\n");
        foreach (var v in valid)
        {
            var value = MarkdownText.Plain(v.Value, 60).Replace(';', ',');
            css.Append("  ").Append(v.Name).Append(": ").Append(value).Append(";\n");
        }

        css.Append('}');
        sb.AppendLine("### CSS variables").AppendLine();
        sb.AppendLine(MarkdownText.Fence("css", css.ToString())).AppendLine();
    }

    // Breakpoints as code spans.
    private static void AppendBreakpoints(StringBuilder sb, DesignSystem design)
    {
        if (design.Breakpoints.Count == 0)
        {
            return;
        }

        sb.AppendLine("### Breakpoints").AppendLine();
        sb.AppendLine(string.Join(", ", design.Breakpoints.Take(MaxBreakpoints).Select(b => MarkdownText.Code(b, 20)))).AppendLine();
    }
}

