using System.Globalization;
using System.Text;
using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Services.Pipeline;

// Writes the "Motion and animation" part of the scan document. Measured facts (libraries, signals,
// animations and scroll triggers the browser reported) are written by this code; the vision model
// only adds the summary and the list of detected animations, each labelled measured or inferred.
// Every string is escaped, so neither page content nor the model can change the layout.
internal static class MarkdownMotionSection
{
    private const int MaxMeasuredRows = 12;
    private const int MaxTriggerRows = 8;
    private const int MaxSignals = 10;

    // Appends the section, or nothing when there is neither evidence nor AI notes.
    public static void Append(StringBuilder sb, VisualCapture? visual, MotionNotes? notes)
    {
        var motion = visual?.Motion;
        var hasFacts = motion is not null && !motion.IsEmpty;
        var hasNotes = notes is not null && !notes.IsEmpty;
        if (!hasFacts && !hasNotes)
        {
            return;
        }

        sb.AppendLine("## Motion and animation").AppendLine();

        if (visual is { Frames.Count: > 0 })
        {
            sb.Append("*Captured ").Append(Num(visual.Frames.Count)).Append(" screenshots while scrolling (")
                .Append(Num(visual.ScrollStepPixels)).Append(" px apart, ")
                .Append(Num(visual.ViewportWidth)).Append('x').Append(Num(visual.ViewportHeight)).AppendLine(" window).*").AppendLine();
        }

        if (hasNotes && notes!.Summary.Length > 0)
        {
            sb.AppendLine(MarkdownText.Inline(notes.Summary, MotionNotesLimits.SummaryChars)).AppendLine();
        }

        if (hasFacts)
        {
            AppendLibrariesAndSignals(sb, motion!);
        }

        if (hasNotes && notes!.Animations.Count > 0)
        {
            AppendDetected(sb, notes.Animations);
        }

        if (hasFacts)
        {
            AppendMeasured(sb, motion!.Animations);
            AppendTriggers(sb, motion.ScrollTriggers);
        }
    }

    private static void AppendLibrariesAndSignals(StringBuilder sb, MotionEvidence motion)
    {
        sb.Append("**Animation libraries:** ")
            .AppendLine(motion.Libraries.Count == 0
                ? "none detected"
                : string.Join(", ", motion.Libraries.Select(l => MarkdownText.Inline(l, 60))))
            .AppendLine();

        if (motion.Signals.Count > 0)
        {
            foreach (var signal in motion.Signals.Take(MaxSignals))
            {
                sb.Append("- ").AppendLine(MarkdownText.Inline(signal, 140));
            }

            sb.AppendLine();
        }
    }

    // The AI's list: Section | Effect | Trigger | Evidence | Library, then the rebuild hints.
    private static void AppendDetected(StringBuilder sb, IReadOnlyList<DetectedAnimation> animations)
    {
        sb.AppendLine("### Detected animations").AppendLine();
        sb.AppendLine("| Section | Effect | Trigger | Evidence | Library |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var a in animations)
        {
            sb.Append("| ").Append(Cell(a.Section, MotionNotesLimits.SectionChars))
                .Append(" | ").Append(Cell(a.Effect, MotionNotesLimits.EffectChars))
                .Append(" | ").Append(Cell(a.Trigger, 30))
                .Append(" | ").Append(Cell(a.Evidence, 20))
                .Append(" | ").Append(Cell(a.Library, MotionNotesLimits.LibraryChars))
                .AppendLine(" |");
        }

        sb.AppendLine();

        var hints = animations.Where(a => a.Recreate.Length > 0).ToList();
        if (hints.Count > 0)
        {
            sb.AppendLine("**How to rebuild them:**").AppendLine();
            foreach (var a in hints)
            {
                var label = a.Section.Length > 0 ? a.Section : "Animation";
                sb.Append("- **").Append(MarkdownText.Inline(label, MotionNotesLimits.SectionChars)).Append(":** ")
                    .AppendLine(MarkdownText.Inline(a.Recreate, MotionNotesLimits.RecreateChars));
            }

            sb.AppendLine();
        }

        sb.AppendLine("*Evidence: \"measured\" matches timing data the browser reported; \"inferred\" was read from the screenshots and may be wrong.*").AppendLine();
    }

    // What the browser reported: Element | Kind | Animates | Duration | Easing | Repeat.
    private static void AppendMeasured(StringBuilder sb, IReadOnlyList<MeasuredAnimation> animations)
    {
        if (animations.Count == 0)
        {
            return;
        }

        sb.AppendLine("### Measured animations").AppendLine();
        sb.AppendLine("| Element | Kind | Animates | Duration | Easing | Repeat |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var a in animations.Take(MaxMeasuredRows))
        {
            var animates = a.Properties.Count > 0 ? string.Join(", ", a.Properties) : a.Name;
            var duration = a.DurationMs is { } d ? Num(d) + " ms" + (a.DelayMs is > 0 ? " (+" + Num(a.DelayMs.Value) + " ms delay)" : string.Empty) : null;
            sb.Append("| ").Append(CodeCell(a.Target, 80))
                .Append(" | ").Append(Cell(KindName(a.Source), 30))
                .Append(" | ").Append(Cell(animates, 80))
                .Append(" | ").Append(Cell(duration, 40))
                .Append(" | ").Append(CodeCell(a.Easing, 60))
                .Append(" | ").Append(Cell(a.Repeat, 20))
                .AppendLine(" |");
        }

        if (animations.Count > MaxMeasuredRows)
        {
            sb.AppendLine().Append("_And ").Append(Num(animations.Count - MaxMeasuredRows)).AppendLine(" more._");
        }

        sb.AppendLine();
    }

    // GSAP scroll triggers: Element | Start | End | Scrub | Pinned.
    private static void AppendTriggers(StringBuilder sb, IReadOnlyList<ScrollTriggerInfo> triggers)
    {
        if (triggers.Count == 0)
        {
            return;
        }

        sb.AppendLine("### Scroll triggers").AppendLine();
        sb.AppendLine("| Element | Start | End | Scrub | Pinned |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var t in triggers.Take(MaxTriggerRows))
        {
            sb.Append("| ").Append(CodeCell(t.Trigger, 80))
                .Append(" | ").Append(CodeCell(t.Start, 40))
                .Append(" | ").Append(CodeCell(t.End, 40))
                .Append(" | ").Append(Cell(t.Scrub, 20))
                .Append(" | ").Append(t.Pinned ? "yes" : "no")
                .AppendLine(" |");
        }

        sb.AppendLine();
    }

    private static string KindName(string source) => source switch
    {
        "css-animation" => "CSS animation",
        "css-transition" => "CSS transition",
        "web-animation" => "Web Animations API",
        "gsap" => "GSAP tween",
        _ => source,
    };

    // An escaped table cell, or "-" when empty.
    private static string Cell(string? text, int max)
    {
        var value = MarkdownText.Inline(text, max);
        return value.Length == 0 ? MarkdownDocumentBuilder.Blank : value;
    }

    // A code-span table cell, or "-" when empty.
    private static string CodeCell(string? text, int max)
    {
        var value = MarkdownText.Code(text, max, inTable: true);
        return value.Length == 0 ? MarkdownDocumentBuilder.Blank : value;
    }

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
}
