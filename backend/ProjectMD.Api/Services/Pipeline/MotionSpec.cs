using System.Globalization;
using System.Text.RegularExpressions;
using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Services.Pipeline;

// One property of an animation, from a start value to an end value. Name is one of opacity, x, y,
// scale, rotation. Percent is true when x or y is a percentage of the element's own size.
public sealed record MotionProp(string Name, double From, double To, bool Percent = false);

// Everything the code samples need to know about one animation: the properties that change, the
// timing and the easing. Built only from numbers and whitelisted words (see MotionSpecBuilder), never
// from page or model text, so it is safe to write into code. Null timing means "not known".
public sealed record MotionSpec(
    IReadOnlyList<MotionProp> Props,
    int? DurationMs,
    int DelayMs,
    string Ease,
    IReadOnlyList<double>? Bezier,
    double? StaggerSeconds,
    bool Infinite);

// GSAP ease names: the whitelist that is allowed into generated code, and conversions from CSS easing.
public static partial class GsapEase
{
    [GeneratedRegex(@"^(power[1-4]|sine|expo|circ|back|elastic|bounce)\.(in|out|inOut)(\(\d{1,2}(\.\d{1,2})?\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();

    // The ease when it is "none" or a known GSAP name, "none" for "linear", otherwise empty.
    public static string Normalize(string? ease)
    {
        var text = ease?.Trim() ?? string.Empty;
        if (text is "none" or "linear")
        {
            return "none";
        }

        return NameRegex().IsMatch(text) ? text : string.Empty;
    }

    // A GSAP ease close to a CSS easing value, or empty when it cannot be told.
    public static string FromCss(string? css, IReadOnlyList<double>? bezier)
    {
        var text = css?.Trim().ToLowerInvariant() ?? string.Empty;
        switch (text)
        {
            case "linear":
                return "none";
            case "ease":
                return "power1.inOut";
            case "ease-in":
                return "power2.in";
            case "ease-out":
                return "power2.out";
            case "ease-in-out":
                return "power2.inOut";
        }

        if (bezier is not { Count: 4 })
        {
            return string.Empty;
        }

        double x1 = bezier[0], y1 = bezier[1], x2 = bezier[2], y2 = bezier[3];
        var startSlow = y1 < x1 - 0.1;
        var endSlow = y2 > x2 + 0.1 || (y2 >= 0.9 && x2 <= 0.8);
        var family = y1 > 1.05 || y2 < -0.05 ? "back" : y1 >= 0.95 && endSlow ? "expo" : "power2";
        return (startSlow, endSlow) switch
        {
            (true, true) => family + ".inOut",
            (true, false) => family + ".in",
            (false, true) => family + ".out",
            _ => "none",
        };
    }
}

// Builds the MotionSpec for one detected animation: the browser's measured values when the model
// pointed at a measured animation, otherwise the model's own numbers, otherwise null (the snippets
// then use the generic template for the trigger).
public static partial class MotionSpecBuilder
{
    [GeneratedRegex(@"^cubic-bezier\(\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\)$", RegexOptions.CultureInvariant)]
    private static partial Regex BezierRegex();

    [GeneratedRegex(@"^stagger (\d{1,2}(\.\d{1,3})?)s$", RegexOptions.CultureInvariant)]
    private static partial Regex StaggerRegex();

    [GeneratedRegex(@"(translateX|translateY|translate|scaleX|scaleY|scale|rotate)\(([^)]*)\)|matrix\(([^)]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex TransformPartRegex();

    [GeneratedRegex(@"^(-?\d+(\.\d+)?)(px|%|deg)?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberUnitRegex();

    public static MotionSpec? Build(DetectedAnimation animation, MotionEvidence? evidence)
    {
        var measured = animation.MeasuredRef is >= 1 && evidence is not null && animation.MeasuredRef <= evidence.Animations.Count
            ? evidence.Animations[animation.MeasuredRef - 1]
            : null;
        return measured is not null ? FromMeasured(measured) : FromAi(animation.Motion);
    }

    // From what the browser reported: timing, easing, repeat and the keyframe values it could read.
    internal static MotionSpec FromMeasured(MeasuredAnimation m)
    {
        var props = m.Source == "gsap" ? GsapProps(m.Values) : CssProps(m.Values);

        var bezier = ParseBezier(m.Easing);
        var ease = m.Source == "gsap" ? GsapEase.Normalize(m.Easing) : GsapEase.FromCss(m.Easing, bezier);
        var stagger = m.Name is not null && StaggerRegex().Match(m.Name) is { Success: true } s
            ? double.Parse(s.Groups[1].Value, CultureInfo.InvariantCulture)
            : (double?)null;

        return new MotionSpec(
            props,
            m.DurationMs is >= 50 and <= 10_000 ? m.DurationMs : null,
            m.DelayMs is > 0 and <= 10_000 ? m.DelayMs.Value : 0,
            ease,
            m.Source == "gsap" ? null : bezier,
            stagger,
            m.Repeat == "infinite");
    }

    // From the model's reading of the frames; null when it gave nothing usable.
    internal static MotionSpec? FromAi(AiMotion? a)
    {
        if (a is null)
        {
            return null;
        }

        var props = new List<MotionProp>();
        Add(props, "opacity", a.FromOpacity, 1, false);
        Add(props, "x", a.MoveX, 0, false);
        Add(props, "y", a.MoveY, 0, false);
        Add(props, "scale", a.FromScale, 1, false);
        Add(props, "rotation", a.Rotate, 0, false);
        if (props.Count == 0 && a.DurationMs is null && a.Easing.Length == 0 && a.StaggerMs is null)
        {
            return null;
        }

        return new MotionSpec(props, a.DurationMs, 0, a.Easing, null, a.StaggerMs is > 0 ? a.StaggerMs / 1000.0 : null, false);
    }

    private static void Add(List<MotionProp> props, string name, double? from, double identity, bool percent)
    {
        if (from is { } f && Math.Abs(f - identity) > 0.001)
        {
            props.Add(new MotionProp(name, Math.Round(f, 2), identity, percent));
        }
    }

    // GSAP's own numbers: each side is a plain number or missing (identity is used for the missing side).
    private static List<MotionProp> GsapProps(IReadOnlyList<MotionValue>? values)
    {
        var props = new List<MotionProp>();
        foreach (var v in values ?? [])
        {
            var name = v.Property is "xPercent" ? "x" : v.Property is "yPercent" ? "y" : v.Property;
            var percent = v.Property is "xPercent" or "yPercent";
            if (name is not ("opacity" or "x" or "y" or "scale" or "rotation"))
            {
                continue;
            }

            Merge(props, name, Plain(v.From), Plain(v.To), percent);
        }

        return props;
    }

    // CSS keyframe strings: opacity, transform, translate, scale, rotate.
    private static List<MotionProp> CssProps(IReadOnlyList<MotionValue>? values)
    {
        var from = new Dictionary<string, (double Value, bool Percent)>();
        var to = new Dictionary<string, (double Value, bool Percent)>();
        foreach (var v in values ?? [])
        {
            switch (v.Property)
            {
                case "opacity":
                    if (Plain(v.From) is { } fo) { from["opacity"] = (fo, false); }
                    if (Plain(v.To) is { } to2) { to["opacity"] = (to2, false); }
                    break;
                case "transform":
                    ParseTransform(v.From, from);
                    ParseTransform(v.To, to);
                    break;
                case "translate":
                case "scale":
                case "rotate":
                    ParseTransform(v.From is null ? null : v.Property + "(" + v.From + ")", from);
                    ParseTransform(v.To is null ? null : v.Property + "(" + v.To + ")", to);
                    break;
            }
        }

        var props = new List<MotionProp>();
        foreach (var name in from.Keys.Concat(to.Keys).Distinct())
        {
            from.TryGetValue(name, out var f);
            to.TryGetValue(name, out var t);
            var hasF = from.ContainsKey(name);
            var hasT = to.ContainsKey(name);
            Merge(props, name, hasF ? f.Value : null, hasT ? t.Value : null, hasF ? f.Percent : t.Percent);
        }

        return props;
    }

    // Adds the property unless both sides are the same; a missing side becomes the identity value.
    private static void Merge(List<MotionProp> props, string name, double? from, double? to, bool percent)
    {
        var identity = name is "opacity" or "scale" ? 1 : 0;
        var f = Clamp(name, from ?? identity);
        var t = Clamp(name, to ?? identity);
        if (Math.Abs(f - t) > 0.001 && props.All(p => p.Name != name))
        {
            props.Add(new MotionProp(name, f, t, percent));
        }
    }

    // Keeps numbers in a sane range so nothing odd reaches the code.
    private static double Clamp(string name, double value)
    {
        var limit = name switch { "opacity" => 1, "scale" => 10, "rotation" => 3600, _ => 5000 };
        var min = name is "opacity" or "scale" ? 0 : -limit;
        return Math.Round(Math.Clamp(value, min, limit), 2);
    }

    private static double? Plain(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : null;

    // Reads translateX/Y, translate, scale, rotate and matrix() out of a transform string.
    private static void ParseTransform(string? text, Dictionary<string, (double, bool)> into)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (Match m in TransformPartRegex().Matches(text))
        {
            if (m.Groups[3].Success)
            {
                ReadMatrix(m.Groups[3].Value, into);
                continue;
            }

            var args = m.Groups[2].Value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(a => NumberUnitRegex().Match(a)).ToList();
            if (args.Count == 0 || args.Any(a => !a.Success))
            {
                continue;
            }

            double N(int i) => double.Parse(args[i].Groups[1].Value, CultureInfo.InvariantCulture);
            bool Pct(int i) => args[i].Groups[3].Value == "%";
            switch (m.Groups[1].Value)
            {
                case "translateX": into["x"] = (N(0), Pct(0)); break;
                case "translateY": into["y"] = (N(0), Pct(0)); break;
                case "translate":
                    into["x"] = (N(0), Pct(0));
                    if (args.Count > 1) { into["y"] = (N(1), Pct(1)); }
                    break;
                case "scale": into["scale"] = (N(0), false); break;
                case "rotate": into["rotation"] = (N(0), false); break;
            }
        }
    }

    // matrix(a, b, c, d, tx, ty): offset, uniform scale and rotation.
    private static void ReadMatrix(string text, Dictionary<string, (double, bool)> into)
    {
        var n = text.Split(',', StringSplitOptions.TrimEntries)
            .Select(p => Plain(p)).ToList();
        if (n.Count != 6 || n.Any(v => v is null))
        {
            return;
        }

        var (a, b, tx, ty) = (n[0]!.Value, n[1]!.Value, n[4]!.Value, n[5]!.Value);
        into["x"] = (Math.Round(tx, 1), false);
        into["y"] = (Math.Round(ty, 1), false);
        into["scale"] = (Math.Round(Math.Sqrt(a * a + b * b), 2), false);
        into["rotation"] = (Math.Round(Math.Atan2(b, a) * 180 / Math.PI, 1), false);
    }

    private static IReadOnlyList<double>? ParseBezier(string? easing)
    {
        if (easing is null || BezierRegex().Match(easing) is not { Success: true } m)
        {
            return null;
        }

        var numbers = new List<double>();
        for (var i = 1; i <= 4; i++)
        {
            if (!double.TryParse(m.Groups[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || !double.IsFinite(d) || Math.Abs(d) > 5)
            {
                return null;
            }

            numbers.Add(Math.Round(d, 3));
        }

        return numbers;
    }
}
