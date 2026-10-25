using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Pipeline;

namespace ProjectMD.Api.Tests.Ai;

// The code samples must follow each animation's real values, not one template per trigger.
public sealed class MotionSpecTests
{
    private static DetectedAnimation Note(string trigger, int measuredRef = 0, AiMotion? motion = null) =>
        new("Hero", "Reveal", "Moves in.", trigger, "measured", "GSAP", "", measuredRef, motion);

    private static MotionEvidence Evidence(params MeasuredAnimation[] animations) => new([], animations, [], []);

    [Fact]
    public void Measured_gsap_values_end_up_in_the_code()
    {
        var gsap = new MeasuredAnimation("gsap", "h1", "stagger 0.04s", 900, 200, "expo.out", null, ["yPercent", "opacity"],
            [new MotionValue("yPercent", "100", null), new MotionValue("opacity", "0", null)]);
        var evidence = Evidence(gsap);

        var report = AnimationReportBuilder.Build(new MotionNotes("", [Note("load", 1)], null), evidence)!;

        var item = report.Items[0];
        Assert.Contains("yPercent: 100", item.Gsap);
        Assert.Contains("duration: 0.9", item.Gsap);
        Assert.Contains("delay: 0.2", item.Gsap);
        Assert.Contains("ease: \"expo.out\"", item.Gsap);
        Assert.Contains("stagger: 0.04", item.Gsap);
        Assert.Contains("y: \"100%\"", item.FramerMotion);
        Assert.Contains("duration: 0.9", item.FramerMotion);
        Assert.DoesNotContain("y: 40", item.Gsap);
    }

    [Fact]
    public void Css_keyframes_and_cubic_bezier_are_used()
    {
        var css = new MeasuredAnimation("css-animation", "div.card", "slide", 1200, 0, "cubic-bezier(0.16, 1, 0.3, 1)", null, ["transform", "opacity"],
            [new MotionValue("transform", "translateX(-80px) scale(0.9)", "none"), new MotionValue("opacity", "0", "1")]);

        var item = AnimationReportBuilder.Build(new MotionNotes("", [Note("scroll", 1)], null), Evidence(css))!.Items[0];

        Assert.Contains("x: -80", item.Gsap);
        Assert.Contains("scale: 0.9", item.Gsap);
        Assert.Contains("ease: \"expo.out\"", item.Gsap);
        Assert.Contains("ease: [0.16, 1, 0.3, 1]", item.FramerMotion);
        Assert.Contains("initial={{ x: -80, scale: 0.9, opacity: 0 }}", item.FramerMotion);
    }

    [Fact]
    public void Different_animations_give_different_code()
    {
        var a = new MeasuredAnimation("gsap", "h1", null, 900, 0, "power3.out", null, ["x"], [new MotionValue("x", "-200", null)]);
        var b = new MeasuredAnimation("css-transition", "a.btn", "transform", 200, 0, "ease-out", null, ["transform"], [new MotionValue("transform", "none", "scale(1.1)")]);
        var notes = new MotionNotes("", [Note("load", 1), Note("hover", 2)], null);

        var items = AnimationReportBuilder.Build(notes, Evidence(a, b))!.Items;

        Assert.NotEqual(items[0].Gsap, items[1].Gsap);
        Assert.Contains("x: -200", items[0].Gsap);
        Assert.Contains("scale: 1.1", items[1].Gsap);
        Assert.Contains("duration: 0.2", items[1].Gsap);
    }

    [Fact]
    public void Model_numbers_are_used_when_nothing_was_measured()
    {
        var motion = new AiMotion(120, null, 0, 0.8, null, "power3.out", 1000, null);

        var item = AnimationReportBuilder.Build(new MotionNotes("", [Note("scroll", 0, motion)], null), null)!.Items[0];

        Assert.Contains("x: 120", item.Gsap);
        Assert.Contains("scale: 0.8", item.Gsap);
        Assert.Contains("duration: 1", item.Gsap);
        Assert.DoesNotContain("y: 40", item.Gsap);
    }

    [Fact]
    public void Bad_references_and_hostile_values_fall_back_to_the_template()
    {
        var hostile = new MeasuredAnimation("gsap", "h1", null, 900, 0, "evil\"); alert(1);//", null, ["x"],
            [new MotionValue("x", "1);alert(1)", "abc"), new MotionValue("scale", "9999", null)]);

        var item = AnimationReportBuilder.Build(new MotionNotes("", [Note("load", 7), Note("load", 1)], null), Evidence(hostile))!.Items;

        Assert.Contains("y: 40", item[0].Gsap); // ref 7 does not exist
        foreach (var i in item)
        {
            Assert.DoesNotContain("alert", i.Gsap);
            Assert.DoesNotContain("alert", i.FramerMotion);
            Assert.DoesNotContain("evil", i.Gsap);
        }
    }

    [Theory]
    [InlineData("ease-out", null, "power2.out")]
    [InlineData("linear", null, "none")]
    [InlineData("cubic-bezier(0.42, 0, 1, 1)", new[] { 0.42, 0, 1.0, 1 }, "power2.in")]
    [InlineData("cubic-bezier(0.65, 0, 0.35, 1)", new[] { 0.65, 0, 0.35, 1.0 }, "power2.inOut")]
    public void Css_easing_maps_to_a_gsap_ease(string css, double[]? bezier, string expected) =>
        Assert.Equal(expected, GsapEase.FromCss(css, bezier));

    [Theory]
    [InlineData("power3.out", "power3.out")]
    [InlineData("back.out(1.7)", "back.out(1.7)")]
    [InlineData("linear", "none")]
    [InlineData("x\"); alert(1)", "")]
    public void Only_known_gsap_eases_are_kept(string input, string expected) =>
        Assert.Equal(expected, GsapEase.Normalize(input));
}
