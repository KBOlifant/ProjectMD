using System.Globalization;
using System.Text;

namespace ProjectMD.Api.Services.Pipeline;

// Code for rebuilding an animation with GSAP or Framer Motion. The properties, distances, timing and
// easing come from the MotionSpec (the browser's measured values, or the model's numbers); only when
// there is no spec does it fall back to a generic template for the trigger word. The inputs are
// numbers and whitelisted words only (the trigger is one of MotionNotesLimits.Triggers), so no text
// from the page or from the AI can end up inside the code. The result is shown as text and copied;
// it is never run by the server. The ".target" selector is a placeholder the reader replaces.
internal static class AnimationSnippets
{
    // The default duration in milliseconds when nothing measured one.
    public const int DefaultDurationMs = 600;

    private const string Target = "\".target\"";

    public static string Gsap(string trigger, int durationMs) => Gsap(trigger, null, durationMs);

    public static string FramerMotion(string trigger, int durationMs) => FramerMotion(trigger, null, durationMs);

    // GSAP code for the trigger, using the spec's values where there are any.
    public static string Gsap(string trigger, MotionSpec? spec, int fallbackMs)
    {
        var r = Resolve(trigger, spec, fallbackMs);
        var opts = GsapOptions(r, includeDuration: trigger != "scroll-scrub");
        var from = GsapVars(r.Props, useFrom: true);
        var to = GsapVars(r.Props, useFrom: false);
        var sb = new StringBuilder();
        switch (trigger)
        {
            case "scroll":
                sb.Append("gsap.registerPlugin(ScrollTrigger);\n\n");
                AppendTween(sb, Target, r, from, to, opts + ",\n  scrollTrigger: { trigger: " + Target + ", start: \"top 80%\" }");
                break;
            case "scroll-scrub":
                sb.Append("gsap.registerPlugin(ScrollTrigger);\n\n");
                AppendTween(sb, Target, r, from, to, opts + ",\n  scrollTrigger: {\n    trigger: " + Target + ",\n    start: \"top bottom\",\n    end: \"bottom top\",\n    scrub: true,\n  }", forceTo: true);
                break;
            case "hover":
                sb.Append("const el = document.querySelector(").Append(Target).Append(");\n")
                    .Append("el.addEventListener(\"mouseenter\", () => gsap.to(el, { ").Append(Inline(to, opts)).Append(" }));\n")
                    .Append("el.addEventListener(\"mouseleave\", () => gsap.to(el, { ").Append(Inline(from, opts)).Append(" }));");
                break;
            case "click":
                sb.Append("const el = document.querySelector(").Append(Target).Append(");\n")
                    .Append("el.addEventListener(\"click\", () => {\n  gsap.fromTo(el, { ").Append(from).Append(" }, {\n    ").Append(to).Append(",\n  ").Append(opts.Replace("\n  ", "\n    ")).Append("\n  });\n});");
                break;
            case "loop":
                AppendTween(sb, Target, r, from, to, opts + ",\n  repeat: -1" + (OnlyRotation(r) ? string.Empty : ",\n  yoyo: true"), forceTo: true);
                break;
            default:
                AppendTween(sb, Target, r, from, to, opts);
                break;
        }

        if (r.Stagger is { } stagger)
        {
            sb.Append("\n// Several elements: add stagger: ").Append(Fmt(stagger)).Append(" to the options so they start one after another.");
        }

        return sb.ToString();
    }

    // Framer Motion (React) code for the trigger, using the spec's values where there are any.
    public static string FramerMotion(string trigger, MotionSpec? spec, int fallbackMs)
    {
        var r = Resolve(trigger, spec, fallbackMs);
        var from = FramerVars(r.Props, useFrom: true);
        var to = FramerVars(r.Props, useFrom: false);
        var transition = FramerTransition(r, trigger);
        var stagger = r.Stagger is { } s ? "\n// Parent of several items: transition={{ staggerChildren: " + Fmt(s) + " }}" : string.Empty;
        return trigger switch
        {
            "scroll" =>
                $"<motion.div\n  initial={{{{ {from} }}}}\n  whileInView={{{{ {to} }}}}\n  viewport={{{{ once: true, amount: 0.3 }}}}\n  transition={{{{ {transition} }}}}\n/>{stagger}",
            "scroll-scrub" => FramerScrub(r),
            "hover" =>
                $"<motion.div\n  whileHover={{{{ {to} }}}}\n  transition={{{{ {transition} }}}}\n/>",
            "click" =>
                $"<motion.button\n  whileTap={{{{ {from} }}}}\n  transition={{{{ {transition} }}}}\n/>",
            "loop" =>
                $"<motion.div\n  animate={{{{ {FramerLoopVars(r.Props)} }}}}\n  transition={{{{ {transition} }}}}\n/>",
            _ =>
                $"<motion.div\n  initial={{{{ {from} }}}}\n  animate={{{{ {to} }}}}\n  transition={{{{ {transition} }}}}\n/>{stagger}",
        };
    }

    // Milliseconds as seconds with at most two decimals, for example 600 -> "0.6".
    internal static string Seconds(int durationMs) =>
        (Math.Clamp(durationMs, 100, 5000) / 1000.0).ToString("0.##", CultureInfo.InvariantCulture);

    // The values to use: the spec's, filled in with generic defaults for the trigger where missing.
    private sealed record Resolved(
        IReadOnlyList<MotionProp> Props,
        string Duration,
        string? Delay,
        string Ease,
        IReadOnlyList<double>? Bezier,
        double? Stagger,
        bool Infinite);

    private static Resolved Resolve(string trigger, MotionSpec? spec, int fallbackMs)
    {
        var props = spec is { Props.Count: > 0 } ? spec.Props : DefaultProps(trigger);
        var ease = spec is not null && spec.Ease.Length > 0 ? spec.Ease : trigger is "scroll-scrub" or "loop" ? "none" : "power2.out";
        return new Resolved(
            props,
            Seconds(spec?.DurationMs ?? (trigger == "loop" && spec is null ? 8000 : fallbackMs)),
            spec is { DelayMs: > 0 } ? Seconds(spec.DelayMs) : null,
            ease,
            spec?.Bezier,
            spec?.StaggerSeconds,
            spec?.Infinite ?? false);
    }

    // Generic starting points per trigger, used only when nothing was measured or seen.
    private static IReadOnlyList<MotionProp> DefaultProps(string trigger) => trigger switch
    {
        "scroll-scrub" => [new MotionProp("y", 0, -120)],
        "hover" => [new MotionProp("scale", 1, 1.05)],
        "click" => [new MotionProp("scale", 0.95, 1)],
        "loop" => [new MotionProp("rotation", 0, 360)],
        "page-transition" => [new MotionProp("opacity", 0, 1)],
        _ => [new MotionProp("opacity", 0, 1), new MotionProp("y", 40, 0)],
    };

    private static bool OnlyRotation(Resolved r) => r.Props.All(p => p.Name == "rotation");

    // gsap.from / gsap.to / gsap.fromTo, whichever matches the values.
    private static void AppendTween(StringBuilder sb, string target, Resolved r, string from, string to, string opts, bool forceTo = false)
    {
        var endsAtRest = r.Props.All(p => Math.Abs(p.To - Identity(p.Name)) < 0.001);
        var startsAtRest = r.Props.All(p => Math.Abs(p.From - Identity(p.Name)) < 0.001);
        if (!forceTo && endsAtRest)
        {
            sb.Append("gsap.from(").Append(target).Append(", {\n  ").Append(from).Append(",\n  ").Append(opts).Append(",\n});");
        }
        else if (startsAtRest || forceTo)
        {
            sb.Append("gsap.to(").Append(target).Append(", {\n  ").Append(to).Append(",\n  ").Append(opts).Append(",\n});");
        }
        else
        {
            sb.Append("gsap.fromTo(").Append(target).Append(", { ").Append(from).Append(" }, {\n  ").Append(to).Append(",\n  ").Append(opts).Append(",\n});");
        }
    }

    private static string Inline(string vars, string opts) => vars + ", " + opts.Replace("\n  ", " ").Trim();

    private static string GsapOptions(Resolved r, bool includeDuration)
    {
        var parts = new List<string>();
        if (includeDuration)
        {
            parts.Add("duration: " + r.Duration);
        }

        if (r.Delay is not null)
        {
            parts.Add("delay: " + r.Delay);
        }

        parts.Add("ease: \"" + r.Ease + "\"");
        if (r.Infinite)
        {
            parts.Add("repeat: -1");
        }

        return string.Join(",\n  ", parts);
    }

    // "opacity: 0, y: 40" for GSAP (percent offsets use xPercent / yPercent).
    private static string GsapVars(IReadOnlyList<MotionProp> props, bool useFrom) =>
        string.Join(", ", props.Select(p => GsapName(p) + ": " + Fmt(useFrom ? p.From : p.To)));

    private static string GsapName(MotionProp p) => p.Name switch
    {
        "x" or "y" when p.Percent => p.Name + "Percent",
        _ => p.Name,
    };

    // "opacity: 0, y: 40" for Framer Motion (rotation is "rotate"; percent offsets are strings).
    private static string FramerVars(IReadOnlyList<MotionProp> props, bool useFrom) =>
        string.Join(", ", props.Select(p => FramerName(p) + ": " + FramerValue(p, useFrom ? p.From : p.To)));

    private static string FramerName(MotionProp p) => p.Name == "rotation" ? "rotate" : p.Name;

    private static string FramerValue(MotionProp p, double value) =>
        p.Percent ? "\"" + Fmt(value) + "%\"" : Fmt(value);

    // Looping: animate between the two values (rotation just keeps turning to the end value).
    private static string FramerLoopVars(IReadOnlyList<MotionProp> props) =>
        string.Join(", ", props.Select(p => FramerName(p) + ": " +
            (p.Name == "rotation" ? FramerValue(p, p.To) : "[" + FramerValue(p, p.From) + ", " + FramerValue(p, p.To) + "]")));

    private static string FramerTransition(Resolved r, string trigger)
    {
        var parts = new List<string>();
        if (trigger != "scroll-scrub")
        {
            parts.Add("duration: " + r.Duration);
        }

        if (r.Delay is not null)
        {
            parts.Add("delay: " + r.Delay);
        }

        parts.Add("ease: " + FramerEase(r));
        if (trigger == "loop" || r.Infinite)
        {
            parts.Add("repeat: Infinity");
            if (!OnlyRotation(r))
            {
                parts.Add("repeatType: \"reverse\"");
            }
        }

        return string.Join(", ", parts);
    }

    // The exact cubic-bezier when the page used one, otherwise the closest named Framer ease.
    private static string FramerEase(Resolved r)
    {
        if (r.Bezier is { Count: 4 })
        {
            return "[" + string.Join(", ", r.Bezier.Select(Fmt)) + "]";
        }

        var name = r.Ease;
        if (name == "none")
        {
            return "\"linear\"";
        }

        var dot = name.IndexOf('.');
        var family = dot > 0 ? name[..dot] : "power";
        var direction = dot > 0 ? name[(dot + 1)..].Split('(')[0] : "out";
        var suffix = direction switch { "in" => "In", "inOut" => "InOut", _ => "Out" };
        return family switch
        {
            "back" => "\"back" + suffix + "\"",
            "circ" => "\"circ" + suffix + "\"",
            _ => "\"ease" + suffix + "\"",
        };
    }

    // Scroll-linked values: one useTransform per property, all driven by the scroll position.
    private static string FramerScrub(Resolved r)
    {
        var sb = new StringBuilder("const { scrollYProgress } = useScroll({ target: ref, offset: [\"start end\", \"end start\"] });\n");
        foreach (var p in r.Props)
        {
            var name = FramerName(p);
            sb.Append("const ").Append(name).Append(" = useTransform(scrollYProgress, [0, 1], [")
                .Append(FramerValue(p, p.From)).Append(", ").Append(FramerValue(p, p.To)).Append("]);\n");
        }

        sb.Append("\n<motion.div ref={ref} style={{ ").Append(string.Join(", ", r.Props.Select(FramerName))).Append(" }} />");
        return sb.ToString();
    }

    private static double Identity(string name) => name is "opacity" or "scale" ? 1 : 0;

    // A number with at most two decimals and no exponent, for example 0.6 or -40.
    private static string Fmt(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
