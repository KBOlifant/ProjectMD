using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Pipeline;

namespace ProjectMD.Api.Tests.Ai;

// Tests for the animation report: the library estimate, the typical duration and the code snippets.
public class AnimationReportTests
{
    private static DetectedAnimation Animation(string trigger, string library = "", string name = "Reveal") =>
        new("Hero", name, "Fades in.", trigger, "inferred", library, "Fade it in.");

    private static MeasuredAnimation Measured(int? ms) =>
        new("css-transition", "div.a", "opacity", ms, null, "ease", null, ["opacity"]);

    private static MotionEvidence Evidence(IReadOnlyList<string> libraries, params int?[] durations) =>
        new(libraries, durations.Select(Measured).ToList(), [], []);

    // The AI's library hint wins, then the page's libraries, then the trigger.
    [Theory]
    [InlineData("load", "GSAP", "GSAP")]
    [InlineData("load", "ScrollTrigger", "GSAP")]
    [InlineData("load", "framer-motion", "Framer Motion")]
    [InlineData("hover", "CSS", "CSS")]
    [InlineData("scroll-scrub", "", "GSAP")]
    [InlineData("hover", "", "CSS")]
    [InlineData("loop", "", "CSS")]
    [InlineData("load", "", "Framer Motion")]
    [InlineData("scroll", "", "Framer Motion")]
    public void Estimate_uses_hint_then_trigger(string trigger, string library, string expected)
    {
        Assert.Equal(expected, AnimationReportBuilder.Estimate(Animation(trigger, library), []));
    }

    // Libraries found on the page decide when the AI gave no hint.
    [Fact]
    public void Estimate_uses_page_libraries()
    {
        Assert.Equal("GSAP", AnimationReportBuilder.Estimate(Animation("load"), ["GSAP", "Lenis"]));
        Assert.Equal("Framer Motion", AnimationReportBuilder.Estimate(Animation("hover"), ["Framer Motion"]));
    }

    // Nothing to report gives null.
    [Fact]
    public void Build_returns_null_without_animations_or_libraries()
    {
        Assert.Null(AnimationReportBuilder.Build(null, null));
        Assert.Null(AnimationReportBuilder.Build(new MotionNotes("Calm page.", []), Evidence([])));
    }

    // Every described animation gets an estimate and both snippets, built from the median duration.
    [Fact]
    public void Build_adds_estimate_and_snippets_to_each_animation()
    {
        var notes = new MotionNotes("Lots of motion.", [Animation("scroll"), Animation("hover")]);

        var report = AnimationReportBuilder.Build(notes, Evidence(["GSAP"], 200, 800, 400));

        Assert.Equal("Lots of motion.", report!.Summary);
        Assert.Equal(["GSAP"], report.Libraries);
        Assert.Equal(2, report.Items.Count);
        Assert.All(report.Items, i => Assert.Equal("GSAP", i.Estimate));
        Assert.Contains("duration: 0.4", report.Items[0].Gsap);
        Assert.Contains("scrollTrigger", report.Items[0].Gsap);
        Assert.Contains("whileInView", report.Items[0].FramerMotion);
        Assert.Contains("mouseenter", report.Items[1].Gsap);
    }

    // Libraries alone (no AI notes) still give a report with no items.
    [Fact]
    public void Build_keeps_measured_libraries_without_notes()
    {
        var report = AnimationReportBuilder.Build(null, Evidence(["Lenis"]));

        Assert.Equal(["Lenis"], report!.Libraries);
        Assert.Empty(report.Items);
    }

    // Without a measured duration the default is used; odd durations are ignored and clamped.
    [Fact]
    public void Duration_defaults_and_clamps()
    {
        Assert.Equal(AnimationSnippets.DefaultDurationMs, AnimationReportBuilder.TypicalDurationMs(null));
        Assert.Equal(AnimationSnippets.DefaultDurationMs, AnimationReportBuilder.TypicalDurationMs(Evidence([], null, 5, 99_999)));
        Assert.Equal("0.1", AnimationSnippets.Seconds(1));
        Assert.Equal("5", AnimationSnippets.Seconds(90_000));
        Assert.Equal("0.6", AnimationSnippets.Seconds(600));
    }

    // Text from the AI (name, description...) never appears inside the code.
    [Fact]
    public void Snippets_never_contain_ai_text()
    {
        var notes = new MotionNotes("s", [Animation("load", "GSAP", name: "EVIL_NAME_alert(1)")]);

        var item = AnimationReportBuilder.Build(notes, null)!.Items[0];

        Assert.DoesNotContain("EVIL_NAME", item.Gsap);
        Assert.DoesNotContain("EVIL_NAME", item.FramerMotion);
    }

    // Every trigger gives code for both libraries, and the JSX braces are doubled correctly.
    [Theory]
    [InlineData("load")]
    [InlineData("scroll")]
    [InlineData("scroll-scrub")]
    [InlineData("hover")]
    [InlineData("click")]
    [InlineData("loop")]
    [InlineData("page-transition")]
    [InlineData("unknown")]
    public void Snippets_exist_for_every_trigger(string trigger)
    {
        Assert.Contains(".target", AnimationSnippets.Gsap(trigger, 600));
        var framer = AnimationSnippets.FramerMotion(trigger, 600);
        Assert.Contains("motion.", framer);
        Assert.DoesNotContain("{{{", framer);
    }
}
