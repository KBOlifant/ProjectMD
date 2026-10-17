using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Extraction.Visual;

namespace ProjectMD.Api.Tests.Extraction;

// Tests for turning the in-page motion script's answer, the script addresses and the CSS into
// MotionEvidence. Pure: no browser.
public class MotionEvidenceReaderTests
{
    private static readonly IReadOnlyList<string> None = [];

    // An answer shaped like the one MotionScript returns for a typical GSAP site.
    private const string GsapAnswer = """
        {
          "libs": { "gsap": true, "scrollTrigger": true, "lenis": true, "three": false },
          "counts": { "canvas": 2, "video": 0, "split": 48, "smil": 0, "willChange": 5, "sticky": 1 },
          "animations": [
            { "source": "css-animation", "target": "div#spinner.loader", "name": "spin", "durationMs": 1200, "delayMs": 0,
              "easing": "linear", "repeat": "infinite", "props": ["transform"] },
            { "source": "gsap", "target": "h1.hero-title", "name": "stagger 0.04s", "durationMs": 900, "delayMs": 200,
              "easing": "power3.out", "repeat": null, "props": ["yPercent", "opacity"] },
            { "source": "made-up", "target": "x" }
          ],
          "triggers": [
            { "trigger": "section#work.work", "start": "top 80%", "end": "bottom top", "scrub": "true", "pinned": true }
          ]
        }
        """;

    [Fact]
    public void Reads_libraries_animations_triggers_and_counts()
    {
        var evidence = MotionEvidenceReader.Read(GsapAnswer, None, None);

        Assert.NotNull(evidence);
        Assert.Equal(["GSAP", "GSAP ScrollTrigger", "Lenis (smooth scroll)"], evidence.Libraries);
        Assert.Equal(2, evidence.Animations.Count);

        var spin = evidence.Animations[0];
        Assert.Equal("css-animation", spin.Source);
        Assert.Equal("spin", spin.Name);
        Assert.Equal(1200, spin.DurationMs);
        Assert.Equal("infinite", spin.Repeat);
        Assert.Equal(["transform"], spin.Properties);

        var hero = evidence.Animations[1];
        Assert.Equal("gsap", hero.Source);
        Assert.Equal("power3.out", hero.Easing);
        Assert.Equal(200, hero.DelayMs);

        var trigger = Assert.Single(evidence.ScrollTriggers);
        Assert.Equal("top 80%", trigger.Start);
        Assert.True(trigger.Pinned);

        Assert.Contains(evidence.Signals, s => s.StartsWith("2 <canvas>", StringComparison.Ordinal));
        Assert.Contains(evidence.Signals, s => s.StartsWith("48 split-text", StringComparison.Ordinal));
        Assert.DoesNotContain(evidence.Signals, s => s.Contains("<video>", StringComparison.Ordinal));
    }

    [Fact]
    public void Script_addresses_reveal_libraries_that_are_not_on_window()
    {
        var scripts = new[]
        {
            "https://cdn.jsdelivr.net/npm/gsap@3.12.5/dist/gsap.min.js",
            "https://cdn.jsdelivr.net/npm/gsap@3.12.5/dist/ScrollTrigger.min.js",
            "https://unpkg.com/three@0.160.0/build/three.module.js",
            "https://example.com/assets/app.js",
        };

        var evidence = MotionEvidenceReader.Read(null, scripts, None);

        Assert.NotNull(evidence);
        Assert.Contains("GSAP", evidence.Libraries);
        Assert.Contains("GSAP ScrollTrigger", evidence.Libraries);
        Assert.Contains("Three.js (WebGL)", evidence.Libraries);
        Assert.Equal(evidence.Libraries.Count, evidence.Libraries.Distinct().Count());
    }

    [Fact]
    public void Css_text_gives_keyframe_transition_and_easing_counts()
    {
        var css = """
            @keyframes fade { from { opacity: 0 } to { opacity: 1 } }
            @-webkit-keyframes slide { from { transform: translateX(-10px) } }
            .btn { transition: transform .3s cubic-bezier(.2,.8,.2,1); }
            .card { transition: opacity 200ms ease; -webkit-transition: none; }
            .reveal { animation-timeline: view(); }
            @media (prefers-reduced-motion: reduce) { * { animation: none !important; } }
            """;

        var evidence = MotionEvidenceReader.Read(null, None, [css]);

        Assert.NotNull(evidence);
        Assert.Contains("2 @keyframes rules in the CSS", evidence.Signals);
        Assert.Contains("2 CSS transition declarations", evidence.Signals);
        Assert.Contains("1 custom cubic-bezier easing curves", evidence.Signals);
        Assert.Contains("CSS scroll-driven animations (animation-timeline)", evidence.Signals);
        Assert.Contains("Respects prefers-reduced-motion in the CSS", evidence.Signals);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"libs\": 5, \"animations\": \"x\"}")]
    public void Garbage_or_nothing_gives_null(string? answer)
    {
        Assert.Null(MotionEvidenceReader.Read(answer, None, None));
    }

    [Fact]
    public void Page_strings_are_cleaned_and_cut()
    {
        var longTarget = new string('a', 500);
        var answer = "{\"animations\":[{\"source\":\"css-transition\",\"target\":\"" + longTarget + "\",\"name\":\"op\\nacity\"}]}";

        var evidence = MotionEvidenceReader.Read(answer, None, None);

        var animation = Assert.Single(evidence!.Animations);
        Assert.True(animation.Target.Length <= 120);
        Assert.Equal("op acity", animation.Name);
    }

    [Fact]
    public void Merge_joins_without_repeats_and_keeps_the_later_counts()
    {
        var first = new MotionEvidence(
            ["GSAP"],
            [new MeasuredAnimation("gsap", "h1", null, 900, 0, "power2.out", null, [])],
            [],
            ["2 <canvas> elements (drawn graphics, often WebGL)", "3 @keyframes rules in the CSS"]);
        var second = new MotionEvidence(
            ["GSAP", "GSAP ScrollTrigger"],
            [
                new MeasuredAnimation("gsap", "h1", null, 900, 0, "power2.out", null, []),
                new MeasuredAnimation("gsap", "section.work", null, 1500, 0, "none", null, []),
            ],
            [new ScrollTriggerInfo("section.work", "top top", "+=100%", "true", true)],
            ["5 <canvas> elements (drawn graphics, often WebGL)"]);

        var merged = MotionEvidenceReader.Merge(first, second)!;

        Assert.Equal(["GSAP", "GSAP ScrollTrigger"], merged.Libraries);
        Assert.Equal(2, merged.Animations.Count);
        Assert.Single(merged.ScrollTriggers);
        Assert.Contains("5 <canvas> elements (drawn graphics, often WebGL)", merged.Signals);
        Assert.DoesNotContain("2 <canvas> elements (drawn graphics, often WebGL)", merged.Signals);
        Assert.Contains("3 @keyframes rules in the CSS", merged.Signals);
    }

    [Fact]
    public void Merge_with_a_missing_side_returns_the_other()
    {
        var one = new MotionEvidence(["GSAP"], [], [], []);

        Assert.Same(one, MotionEvidenceReader.Merge(one, null));
        Assert.Same(one, MotionEvidenceReader.Merge(null, one));
        Assert.Null(MotionEvidenceReader.Merge(null, null));
    }
}
