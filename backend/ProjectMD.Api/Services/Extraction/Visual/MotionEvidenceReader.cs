using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectMD.Api.Services.Extraction.Visual;

// Turns the answer of MotionScript, the script addresses and the stylesheet text into a small,
// safe MotionEvidence. Pure: no browser, no network. Nothing from the page is trusted: every string
// is cut to a short length, control characters are removed, and lists are capped.
public static partial class MotionEvidenceReader
{
    public const int MaxAnimations = 40;
    public const int MaxScrollTriggers = 20;
    private const int MaxJsonChars = 200_000;
    private const int MaxCssChars = 2_000_000;
    private const int MaxTextChars = 120;
    private const int MaxProperties = 8;

    // Display names for the flags the script reports, in the order they are listed.
    private static readonly (string Flag, string Name)[] LibraryFlags =
    [
        ("gsap", "GSAP"),
        ("scrollTrigger", "GSAP ScrollTrigger"),
        ("splitText", "Split text (SplitText / SplitType)"),
        ("lenis", "Lenis (smooth scroll)"),
        ("locomotive", "Locomotive Scroll"),
        ("barba", "Barba.js (page transitions)"),
        ("swup", "Swup (page transitions)"),
        ("three", "Three.js (WebGL)"),
        ("pixi", "PixiJS (WebGL)"),
        ("lottie", "Lottie"),
        ("rive", "Rive"),
        ("spline", "Spline (3D)"),
        ("anime", "anime.js"),
        ("aos", "AOS (animate on scroll)"),
        ("webflowIx", "Webflow Interactions"),
        ("framer", "Framer"),
        ("swiper", "Swiper (carousel)"),
    ];

    // Script address patterns (lower case) that reveal a library even when it is not on window.
    private static readonly (Regex Pattern, string Name)[] ScriptPatterns =
    [
        (ScriptRegex(@"(^|[/._-])gsap([/@._-]|$)"), "GSAP"),
        (ScriptRegex(@"scrolltrigger"), "GSAP ScrollTrigger"),
        (ScriptRegex(@"splittext|split-type|splittype"), "Split text (SplitText / SplitType)"),
        (ScriptRegex(@"(^|[/._-])lenis([/@._-]|$)|studio-freight"), "Lenis (smooth scroll)"),
        (ScriptRegex(@"locomotive-scroll"), "Locomotive Scroll"),
        (ScriptRegex(@"(^|[/._-])barba([/@._-]|$)|@barba"), "Barba.js (page transitions)"),
        (ScriptRegex(@"(^|[/._-])swup([/@._-]|$)"), "Swup (page transitions)"),
        (ScriptRegex(@"(^|/)three([/@._-]|$)|three\.module|three\.min"), "Three.js (WebGL)"),
        (ScriptRegex(@"pixi(\.min)?\.js|/pixi\.js|@pixi"), "PixiJS (WebGL)"),
        (ScriptRegex(@"lottie|bodymovin"), "Lottie"),
        (ScriptRegex(@"@rive-app|/rive\.js"), "Rive"),
        (ScriptRegex(@"@splinetool|spline-viewer"), "Spline (3D)"),
        (ScriptRegex(@"animejs|anime(\.min)?\.js"), "anime.js"),
        (ScriptRegex(@"(^|/)aos([/@.]|$)"), "AOS (animate on scroll)"),
        (ScriptRegex(@"framer-motion"), "Framer Motion"),
        (ScriptRegex(@"swiper"), "Swiper (carousel)"),
    ];

    private static Regex ScriptRegex(string pattern) => new(pattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [GeneratedRegex(@"@(-webkit-|-moz-)?keyframes\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyframesRegex();

    [GeneratedRegex(@"(?<![-\w])transition\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TransitionRegex();

    [GeneratedRegex(@"cubic-bezier\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CubicBezierRegex();

    [GeneratedRegex(@"(animation|scroll|view)-timeline\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScrollTimelineRegex();

    [GeneratedRegex(@"view-transition-name\s*:|@view-transition", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ViewTransitionRegex();

    [GeneratedRegex(@"prefers-reduced-motion", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReducedMotionRegex();

    // Builds the evidence. `scriptJson` is MotionScript's answer (null or garbage just means the
    // in-page part is missing). Returns null only when nothing at all was found.
    public static MotionEvidence? Read(string? scriptJson, IReadOnlyList<string> scriptUrls, IReadOnlyList<string> stylesheets)
    {
        var libraries = new List<string>();
        var animations = new List<MeasuredAnimation>();
        var triggers = new List<ScrollTriggerInfo>();
        var signals = new List<string>();

        ReadScript(scriptJson, libraries, animations, triggers, signals);

        foreach (var url in scriptUrls)
        {
            var lower = url.ToLowerInvariant();
            foreach (var (pattern, name) in ScriptPatterns)
            {
                if (pattern.IsMatch(lower))
                {
                    AddOnce(libraries, name);
                }
            }
        }

        AddCssSignals(stylesheets, signals);

        var evidence = new MotionEvidence(libraries, animations, triggers, signals);
        return evidence.IsEmpty ? null : evidence;
    }

    // Combines the evidence read right after loading with the evidence read after scrolling (scroll
    // animations only exist once they are reached). Lists are joined without repeats and capped.
    public static MotionEvidence? Merge(MotionEvidence? first, MotionEvidence? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        var libraries = first.Libraries.Concat(second.Libraries).Distinct(StringComparer.Ordinal).ToList();
        var animations = first.Animations.Concat(second.Animations)
            .DistinctBy(a => (a.Source, a.Target, a.Name, a.DurationMs))
            .Take(MaxAnimations)
            .ToList();
        var triggers = first.ScrollTriggers.Concat(second.ScrollTriggers)
            .DistinctBy(t => (t.Trigger, t.Start, t.End))
            .Take(MaxScrollTriggers)
            .ToList();

        // Counts can only grow while scrolling (lazy content), so the later list of signals wins
        // where both have one; signals only the first had are kept.
        var signals = second.Signals.ToList();
        foreach (var signal in first.Signals)
        {
            if (!signals.Any(s => SignalKey(s) == SignalKey(signal)))
            {
                signals.Add(signal);
            }
        }

        return new MotionEvidence(libraries, animations, triggers, signals);
    }

    // The part of a signal after its leading number, so "3 <canvas> elements" and "5 <canvas>
    // elements" count as the same signal.
    private static string SignalKey(string signal) => signal.TrimStart("0123456789 ".ToCharArray());

    // Reads the in-page answer. Any shape problem simply leaves that part out.
    private static void ReadScript(
        string? json,
        List<string> libraries,
        List<MeasuredAnimation> animations,
        List<ScrollTriggerInfo> triggers,
        List<string> signals)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonChars)
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (root.TryGetProperty("libs", out var libs) && libs.ValueKind == JsonValueKind.Object)
            {
                foreach (var (flag, name) in LibraryFlags)
                {
                    if (libs.TryGetProperty(flag, out var value) && value.ValueKind == JsonValueKind.True)
                    {
                        AddOnce(libraries, name);
                    }
                }
            }

            if (root.TryGetProperty("counts", out var counts) && counts.ValueKind == JsonValueKind.Object)
            {
                AddCount(signals, counts, "canvas", "<canvas> elements (drawn graphics, often WebGL)");
                AddCount(signals, counts, "video", "<video> elements");
                AddCount(signals, counts, "split", "split-text pieces (letters or words wrapped in their own elements)");
                AddCount(signals, counts, "smil", "SVG animation elements (SMIL)");
                AddCount(signals, counts, "willChange", "elements with will-change (prepared for animation)");
                AddCount(signals, counts, "sticky", "sticky elements (stay in place while scrolling)");
            }

            if (root.TryGetProperty("animations", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (animations.Count >= MaxAnimations)
                    {
                        break;
                    }

                    if (ReadAnimation(item) is { } animation)
                    {
                        animations.Add(animation);
                    }
                }
            }

            if (root.TryGetProperty("triggers", out var stList) && stList.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in stList.EnumerateArray())
                {
                    if (triggers.Count >= MaxScrollTriggers)
                    {
                        break;
                    }

                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    triggers.Add(new ScrollTriggerInfo(
                        Text(item, "trigger") ?? "element",
                        Text(item, "start"),
                        Text(item, "end"),
                        Text(item, "scrub"),
                        item.TryGetProperty("pinned", out var pinned) && pinned.ValueKind == JsonValueKind.True));
                }
            }
        }
        catch (JsonException)
        {
            // A broken answer just means no in-page evidence.
        }
    }

    // One animation entry, or null when it is not an object with a known source.
    private static MeasuredAnimation? ReadAnimation(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var source = Text(item, "source");
        if (source is not ("css-animation" or "css-transition" or "web-animation" or "gsap"))
        {
            return null;
        }

        var properties = new List<string>();
        if (item.TryGetProperty("props", out var props) && props.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in props.EnumerateArray())
            {
                if (p.ValueKind == JsonValueKind.String && Clean(p.GetString(), 40) is { Length: > 0 } name && properties.Count < MaxProperties)
                {
                    properties.Add(name);
                }
            }
        }

        return new MeasuredAnimation(
            source,
            Text(item, "target") ?? "element",
            Text(item, "name"),
            Milliseconds(item, "durationMs"),
            Milliseconds(item, "delayMs"),
            Text(item, "easing"),
            Text(item, "repeat"),
            properties,
            ReadValues(item));
    }

    private static readonly HashSet<string> ValueProperties =
        ["opacity", "transform", "translate", "scale", "rotate", "x", "y", "xPercent", "yPercent", "rotation"];

    // The start and end values of the properties that matter; unknown property names are dropped.
    private static List<MotionValue> ReadValues(JsonElement item)
    {
        var values = new List<MotionValue>();
        if (!item.TryGetProperty("values", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return values;
        }

        foreach (var v in list.EnumerateArray())
        {
            if (values.Count >= MaxProperties || v.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var property = Text(v, "p");
            if (property is not null && ValueProperties.Contains(property))
            {
                values.Add(new MotionValue(property, Text(v, "f"), Text(v, "t")));
            }
        }

        return values;
    }

    // A string property, cleaned, or null when missing, empty or not a string.
    private static string? Text(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = Clean(value.GetString(), MaxTextChars);
        return text.Length == 0 ? null : text;
    }

    // A whole number of milliseconds between 0 and 10 minutes, or null.
    private static int? Milliseconds(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var ms))
        {
            return null;
        }

        return ms is >= 0 and <= 600_000 ? (int)Math.Round(ms) : null;
    }

    // Adds "N label" when the count is a positive number.
    private static void AddCount(List<string> signals, JsonElement counts, string name, string label)
    {
        if (counts.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var n) && n > 0)
        {
            signals.Add(n.ToString(CultureInfo.InvariantCulture) + " " + label);
        }
    }

    // Facts read from the stylesheet text the scan already fetched.
    private static void AddCssSignals(IReadOnlyList<string> stylesheets, List<string> signals)
    {
        if (stylesheets.Count == 0)
        {
            return;
        }

        var css = new StringBuilder();
        foreach (var sheet in stylesheets)
        {
            if (css.Length + sheet.Length > MaxCssChars)
            {
                css.Append(sheet.AsSpan(0, Math.Max(0, MaxCssChars - css.Length)));
                break;
            }

            css.Append(sheet).Append('\n');
        }

        var text = css.ToString();
        AddMatches(signals, KeyframesRegex(), text, "@keyframes rules in the CSS");
        AddMatches(signals, TransitionRegex(), text, "CSS transition declarations");
        AddMatches(signals, CubicBezierRegex(), text, "custom cubic-bezier easing curves");
        if (ScrollTimelineRegex().IsMatch(text))
        {
            signals.Add("CSS scroll-driven animations (animation-timeline)");
        }

        if (ViewTransitionRegex().IsMatch(text))
        {
            signals.Add("Native view transitions (view-transition-name)");
        }

        if (ReducedMotionRegex().IsMatch(text))
        {
            signals.Add("Respects prefers-reduced-motion in the CSS");
        }
    }

    // Adds "N label" for the number of matches, when there are any.
    private static void AddMatches(List<string> signals, Regex regex, string text, string label)
    {
        var n = regex.Count(text);
        if (n > 0)
        {
            signals.Add(n.ToString(CultureInfo.InvariantCulture) + " " + label);
        }
    }

    private static void AddOnce(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.Ordinal))
        {
            list.Add(value);
        }
    }

    // One line of plain text: control characters and line breaks become spaces, cut to maxChars.
    private static string Clean(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(Math.Min(text.Length, maxChars));
        foreach (var c in text)
        {
            if (sb.Length >= maxChars)
            {
                break;
            }

            sb.Append(char.IsControl(c) || char.IsWhiteSpace(c) ? ' ' : c);
        }

        return sb.ToString().Trim();
    }
}
