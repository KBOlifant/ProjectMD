using System.Text;
using System.Text.RegularExpressions;
using ProjectMD.Api.Services.Ai;

namespace ProjectMD.Api.Services.Pipeline;

// Writes the "Design breakdown" part of the scan document: the vision model's reading of the
// visual design (overall style, typography, colours and components). It sits right after the
// measured "Design system" section; the website shows both on its Design tab. Every string is
// escaped, so the model cannot change the layout.
internal static partial class MarkdownDesignBreakdownSection
{
    // The heading the website looks for when it builds the Design tab.
    public const string Heading = "## Design breakdown";

    [GeneratedRegex(@"^#[0-9a-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexRegex();

    // Appends the section, or nothing when there is no breakdown.
    public static void Append(StringBuilder sb, DesignBreakdown? design)
    {
        if (design is null || design.IsEmpty)
        {
            return;
        }

        sb.AppendLine(Heading).AppendLine();
        if (design.Overview.Length > 0)
        {
            sb.AppendLine(MarkdownText.Inline(design.Overview, MotionNotesLimits.DesignOverviewChars)).AppendLine();
        }

        AppendTypography(sb, design.Typography);
        AppendColors(sb, design.Colors);
        AppendComponents(sb, design.Components);
        sb.AppendLine("*Written by AI from the screenshots and the measured design facts. Check details before relying on them.*").AppendLine();
    }

    // Role | Font | Details.
    private static void AppendTypography(StringBuilder sb, IReadOnlyList<TypographyNote> fonts)
    {
        if (fonts.Count == 0)
        {
            return;
        }

        sb.AppendLine("### Typography").AppendLine();
        sb.AppendLine("| Role | Font | Details |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var f in fonts)
        {
            sb.Append("| ").Append(Cell(f.Role, MotionNotesLimits.ShortChars))
                .Append(" | ").Append(Cell(f.Font, MotionNotesLimits.ShortChars))
                .Append(" | ").Append(Cell(f.Details, MotionNotesLimits.DetailsChars))
                .AppendLine(" |");
        }

        sb.AppendLine();
    }

    // Colour | Name | Used for. The hex goes in a code span.
    private static void AppendColors(StringBuilder sb, IReadOnlyList<ColorNote> colors)
    {
        if (colors.Count == 0)
        {
            return;
        }

        sb.AppendLine("### Colour roles").AppendLine();
        sb.AppendLine("| Colour | Name | Used for |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var c in colors)
        {
            var hex = HexRegex().IsMatch(c.Hex) ? "`" + c.Hex + "`" : MarkdownDocumentBuilder.Blank;
            sb.Append("| ").Append(hex)
                .Append(" | ").Append(Cell(c.Name, MotionNotesLimits.ShortChars))
                .Append(" | ").Append(Cell(c.Usage, MotionNotesLimits.DetailsChars))
                .AppendLine(" |");
        }

        sb.AppendLine();
    }

    // One small heading per component with its description and styling.
    private static void AppendComponents(StringBuilder sb, IReadOnlyList<ComponentNote> components)
    {
        if (components.Count == 0)
        {
            return;
        }

        sb.AppendLine("### Components").AppendLine();
        foreach (var c in components)
        {
            sb.Append("- **").Append(MarkdownText.Inline(c.Name, MotionNotesLimits.ShortChars)).Append(":** ")
                .AppendLine(c.Description.Length > 0 ? MarkdownText.Inline(c.Description, MotionNotesLimits.ComponentDescriptionChars) : MarkdownDocumentBuilder.Blank);
            if (c.Styling.Length > 0)
            {
                sb.Append("  - *Styling:* ").AppendLine(MarkdownText.Inline(c.Styling, MotionNotesLimits.DetailsChars));
            }
        }

        sb.AppendLine();
    }

    private static string Cell(string? text, int max)
    {
        var value = MarkdownText.Inline(text, max);
        return value.Length == 0 ? MarkdownDocumentBuilder.Blank : value;
    }
}
