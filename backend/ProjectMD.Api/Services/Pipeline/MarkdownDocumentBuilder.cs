using System.Text;
using ProjectMD.Api.Models;
using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Services.Pipeline;

// How the document in a scan was produced. Decides the footer line at the bottom of the Markdown.
public enum ScanBasis
{
    // `extract` mode: built from the page structure only, no AI was ever asked.
    Extract,

    // The AI wrote the short explanations (fresh or from the cache).
    Ai,

    // `ai` mode, but no AI client is configured, so this is the same as Extract.
    AiNotConfigured,

    // `ai` mode, but a daily token cap was reached, so the AI was skipped.
    AiBudget,

    // `ai` mode, but the AI call failed or returned nothing usable, so the AI was skipped.
    AiFailed,
}

// Everything the renderer needs for one scan, apart from the AI's notes. DurationMs is the real
// time spent so far; Confidence is the heuristic score from ConfidenceEstimator.
public sealed record ScanFacts(
    string Host,
    Uri RequestedUrl,
    DateTime ScannedOnUtc,
    PageDigest Digest,
    IReadOnlyList<ScanTech> Tech,
    int Confidence,
    int DurationMs,
    ScanBasis Basis);

// Builds the whole scan document in one fixed layout. Facts (technologies, endpoints, colours,
// outline) are written by this code from what the scan captured; the AI only supplies short
// explanations, and when there are none (basic scan) the same sections appear with "-" in the
// explanation cells. Same facts and same AI notes always give byte-identical Markdown.
// Every captured or AI-written string is escaped (MarkdownText), so page content cannot change the
// layout.
public static class MarkdownDocumentBuilder
{
    // Footer lines. Every fallback says "Basic scan" so it is easy to spot.
    public const string FooterExtract = "Basic scan: built from the page's own structure, without AI writing.";
    public const string FooterAi = "AI notes: the explanations were written by AI from the page facts above; the facts themselves come from the scan. Check details before relying on them.";
    public const string FooterNotConfigured = "Basic scan: AI writing is not configured.";
    public const string FooterBudget = "Basic scan: the daily AI budget was reached, so no AI writing was used.";
    public const string FooterFailed = "Basic scan: AI writing was unavailable for this scan.";

    // The text shown where an explanation is missing.
    internal const string Blank = "-";

    // Caps that keep the document small however big the page is.
    private const int MaxOutlineItems = 40;
    private const int MaxTechItems = 12;
    private const int MaxAiChars = 500;

    // Builds the Markdown document. `ai` is null for a basic scan. `motion` holds the vision model's
    // animation notes (null when motion AI did not run); the measured motion facts come from the digest.
    public static string Build(ScanFacts facts, AiDocument? ai, MotionNotes? motion = null)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var digest = facts.Digest;
        var sb = new StringBuilder();

        AppendFrontMatter(sb, facts);
        sb.Append("# ").AppendLine(MarkdownText.Inline(facts.Host, 255)).AppendLine();
        sb.AppendLine(SummaryLine(facts)).AppendLine();

        sb.AppendLine("## Overview").AppendLine();
        var overview = ai is { Overview.Length: > 0 }
            ? MarkdownText.Inline(ai.Overview, MaxAiChars)
            : MarkdownText.Inline(digest.MetaDescription, 400);
        sb.AppendLine(overview.Length > 0 ? overview : Blank).AppendLine();

        AppendTechStack(sb, facts);

        if (ai is { Architecture.Count: > 0 })
        {
            sb.AppendLine("## Architecture").AppendLine();
            AppendBullets(sb, ai.Architecture);
        }

        MarkdownApiSection.Append(sb, digest, ai);
        MarkdownDesignSection.Append(sb, digest.Design, ai);
        MarkdownMotionSection.Append(sb, digest.Visual, motion);
        AppendPageStructure(sb, digest.Outline);

        if (ai is { Tips.Count: > 0 })
        {
            sb.AppendLine("## Developer tips").AppendLine();
            AppendBullets(sb, ai.Tips);
        }

        AppendFooter(sb, facts);

        // Always "\n", whatever operating system the server runs on.
        return sb.ToString().ReplaceLineEndings("\n");
    }

    // The footer sentence for how the document was produced.
    public static string FooterFor(ScanBasis basis) => basis switch
    {
        ScanBasis.Ai => FooterAi,
        ScanBasis.AiNotConfigured => FooterNotConfigured,
        ScanBasis.AiBudget => FooterBudget,
        ScanBasis.AiFailed => FooterFailed,
        _ => FooterExtract,
    };

    // The YAML block at the very top: title, url, scan time (UTC), scanner and technology names.
    private static void AppendFrontMatter(StringBuilder sb, ScanFacts facts)
    {
        var title = string.IsNullOrWhiteSpace(facts.Digest.Title) ? facts.Host : facts.Digest.Title;
        sb.AppendLine("---");
        sb.Append("title: ").AppendLine(MarkdownText.YamlString(title));
        sb.Append("url: ").AppendLine(MarkdownText.YamlString(facts.RequestedUrl.AbsoluteUri));
        sb.Append("scanned_at: ").AppendLine(facts.ScannedOnUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append("scanner: ").AppendLine(facts.Basis == ScanBasis.Ai ? "ai" : "basic");
        var tech = TechNames(facts);
        if (tech.Count == 0)
        {
            sb.AppendLine("technologies: []");
        }
        else
        {
            sb.AppendLine("technologies:");
            foreach (var name in tech)
            {
                sb.Append("  - ").AppendLine(MarkdownText.YamlString(name));
            }
        }

        sb.AppendLine("---").AppendLine();
    }

    // One italic line: HTTP status, how long the scan took, and the confidence (labelled a heuristic).
    private static string SummaryLine(ScanFacts facts)
    {
        var seconds = (facts.DurationMs / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        return $"*Status {MarkdownText.Number(facts.Digest.StatusCode)} \u00B7 scanned in {seconds} s \u00B7 "
            + $"confidence {MarkdownText.Number(facts.Confidence)}% (a heuristic estimate of how much the page told us, not a measured accuracy)*";
    }

    // The technology names in display order, capped.
    private static List<string> TechNames(ScanFacts facts) =>
        facts.Tech.Where(t => !string.IsNullOrWhiteSpace(t.Name)).Take(MaxTechItems).Select(t => t.Name.Trim()).ToList();

    // The tech table (Technology | Version | Role) and the rendering line.
    private static void AppendTechStack(StringBuilder sb, ScanFacts facts)
    {
        sb.AppendLine("## Tech stack").AppendLine();
        var tech = TechNames(facts);
        if (tech.Count == 0)
        {
            sb.AppendLine("_No technologies were recognised from the page._").AppendLine();
        }
        else
        {
            sb.AppendLine("| Technology | Version | Role |");
            sb.AppendLine("| --- | --- | --- |");
            foreach (var full in tech)
            {
                var (name, version) = TechRoles.Split(full);
                var role = TechRoles.RoleFor(full) ?? TechRoles.RoleFor(name);
                sb.Append("| ").Append(MarkdownText.Inline(name, 80))
                    .Append(" | ").Append(version is null ? Blank : MarkdownText.Inline(version, 20))
                    .Append(" | ").Append(role is null ? Blank : MarkdownText.Inline(role, 40))
                    .AppendLine(" |");
            }

            sb.AppendLine();
        }

        var hints = RenderingHints.Describe(facts.Digest);
        if (hints.Count > 0)
        {
            sb.Append("**Rendering:** ").Append(string.Join("; ", hints.Select(h => MarkdownText.Inline(h, 160)))).AppendLine(" (guessed from page signals).").AppendLine();
        }
    }

    // The heading outline as a nested list, indented by level relative to the shallowest heading.
    private static void AppendPageStructure(StringBuilder sb, IReadOnlyList<HeadingNode> outline)
    {
        sb.AppendLine("## Page structure").AppendLine();
        var headings = outline.Where(h => !string.IsNullOrWhiteSpace(h.Text)).Take(MaxOutlineItems).ToList();
        if (headings.Count == 0)
        {
            sb.AppendLine("_No headings were found on the page._").AppendLine();
            return;
        }

        var top = headings.Min(h => h.Level);
        foreach (var h in headings)
        {
            var depth = Math.Clamp(h.Level - top, 0, 5);
            sb.Append(' ', depth * 2).Append("- ").AppendLine(MarkdownText.Inline(h.Text, 160));
        }

        sb.AppendLine();
    }

    // A bullet list of AI sentences (escaped), followed by a blank line.
    internal static void AppendBullets(StringBuilder sb, IReadOnlyList<string> items)
    {
        foreach (var item in items)
        {
            sb.Append("- ").AppendLine(MarkdownText.Inline(item, MaxAiChars));
        }

        sb.AppendLine();
    }

    // The closing block: who generated it, a reminder that estimates are heuristics, and how the
    // explanations were produced (AI or basic scan).
    private static void AppendFooter(StringBuilder sb, ScanFacts facts)
    {
        var scanner = facts.Basis == ScanBasis.Ai ? "ai" : "basic";
        sb.AppendLine("---").AppendLine();
        sb.Append("*Generated by Project MD (").Append(scanner).AppendLine(" scan). Confidence and other estimates are heuristics, not measurements.*").AppendLine();
        sb.Append('*').Append(FooterFor(facts.Basis)).AppendLine("*");
    }
}

