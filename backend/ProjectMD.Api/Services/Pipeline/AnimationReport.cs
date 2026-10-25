using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Services.Pipeline;

// One animation for the website's Animations tab: the AI's description (Section, Name, Description,
// Trigger, Evidence, Library, Recreate), our estimate of the library that most likely runs it
// ("GSAP", "Framer Motion" or "CSS"), and ready-to-copy code for GSAP and for Framer Motion.
public sealed record AnimationItem(
    string Section,
    string Name,
    string Description,
    string Trigger,
    string Evidence,
    string Library,
    string Recreate,
    string Estimate,
    string Gsap,
    string FramerMotion);

// The animations of a scan: the AI's one-line summary, the libraries the browser measured on the
// page, and the animations with their estimates and code.
public sealed record AnimationReport(string Summary, IReadOnlyList<string> Libraries, IReadOnlyList<AnimationItem> Items);

// Builds the AnimationReport from the AI's motion notes and the browser's measured evidence. Free
// (no AI call) and pure. The estimate is a guess and the code is a starting point; both are labelled
// that way on the website. The code uses the browser's measured values for the animation the model
// matched (or the model's own numbers), so it differs per animation.
public static class AnimationReportBuilder
{
    public const string EstimateGsap = "GSAP";
    public const string EstimateFramer = "Framer Motion";
    public const string EstimateCss = "CSS";

    // Returns null when there are no described animations and no measured libraries.
    public static AnimationReport? Build(MotionNotes? notes, MotionEvidence? evidence)
    {
        var libraries = evidence?.Libraries ?? [];
        var animations = notes?.Animations ?? [];
        if (animations.Count == 0 && libraries.Count == 0)
        {
            return null;
        }

        var duration = TypicalDurationMs(evidence);
        var items = animations
            .Select(a => (Note: a, Spec: MotionSpecBuilder.Build(a, evidence)))
            .Select(x => new AnimationItem(
                x.Note.Section,
                x.Note.Name,
                x.Note.Description,
                x.Note.Trigger,
                x.Note.Evidence,
                x.Note.Library,
                x.Note.Recreate,
                Estimate(x.Note, libraries),
                AnimationSnippets.Gsap(x.Note.Trigger, x.Spec, duration),
                AnimationSnippets.FramerMotion(x.Note.Trigger, x.Spec, duration)))
            .ToList();
        return new AnimationReport(notes?.Summary ?? string.Empty, libraries, items);
    }

    // Which library most likely runs the animation: the AI's own library hint first, then what the
    // browser found on the page, then the kind of trigger.
    internal static string Estimate(DetectedAnimation animation, IReadOnlyList<string> pageLibraries)
    {
        var hint = animation.Library;
        if (Mentions(hint, "gsap") || Mentions(hint, "scrolltrigger"))
        {
            return EstimateGsap;
        }

        if (Mentions(hint, "framer"))
        {
            return EstimateFramer;
        }

        if (hint.Trim().Equals("css", StringComparison.OrdinalIgnoreCase))
        {
            return EstimateCss;
        }

        if (animation.Trigger == "scroll-scrub" || pageLibraries.Any(l => Mentions(l, "gsap") || Mentions(l, "scrolltrigger")))
        {
            return EstimateGsap;
        }

        if (pageLibraries.Any(l => Mentions(l, "framer")))
        {
            return EstimateFramer;
        }

        return animation.Trigger is "hover" or "loop" ? EstimateCss : EstimateFramer;
    }

    // The median measured duration in milliseconds (between 50 ms and 10 s), or the default.
    internal static int TypicalDurationMs(MotionEvidence? evidence)
    {
        var durations = (evidence?.Animations ?? [])
            .Select(a => a.DurationMs)
            .Where(d => d is >= 50 and <= 10_000)
            .Select(d => d!.Value)
            .Order()
            .ToList();
        return durations.Count == 0 ? AnimationSnippets.DefaultDurationMs : durations[durations.Count / 2];
    }

    private static bool Mentions(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);
}
