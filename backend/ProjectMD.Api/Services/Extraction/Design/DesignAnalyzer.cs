using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectMD.Api.Services.Extraction.Design;

// Turns the raw style counts from the page (DesignSample) and the stylesheet text into the compact
// DesignSystem summary. Everything here is pure (no browser, no network): merge similar colours,
// guess colour roles, clean font names, pick the most used radii, shadows and spacing steps, and keep
// the whole result small. Output order is fixed (most used first, ties broken by value) so the same
// page always gives the same summary and therefore the same content hash.
public static partial class DesignAnalyzer
{
    // Limits from the capture spec.
    public const int MaxColors = 12;
    public const int MaxFonts = 4;
    public const int MaxSizesPerFont = 6;
    public const int MaxWeightsPerFont = 6;
    public const int MaxRadii = 5;
    public const int MaxShadows = 4;
    public const int MaxShadowChars = 80;
    public const int MaxSpacing = 8;
    public const int MaxSpacingPx = 128;

    // The result aims to stay under this many JSON bytes; CSS variables beyond MinVariablesKept are
    // dropped (last first) when it is bigger.
    public const int TargetJsonBytes = 1500;
    public const int MinVariablesKept = 20;

    // A colour must be at least this vivid to be guessed as the accent colour.
    private const double AccentSaturation = 0.35;

    // Font names that are generic fallbacks or system aliases, not a real brand font.
    private static readonly HashSet<string> GenericFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "ui-serif", "ui-sans-serif",
        "ui-monospace", "ui-rounded", "emoji", "math", "fangsong", "-apple-system", "blinkmacsystemfont",
        "inherit", "initial", "unset",
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // Builds the summary from one sample and the stylesheet texts. Returns null when the sample saw
    // no elements at all.
    public static DesignSystem? Build(DesignSample sample, IReadOnlyList<string> stylesheets)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(stylesheets);
        if (sample.Sampled <= 0)
        {
            return null;
        }

        var design = new DesignSystem(
            Colors(sample.Colors),
            Fonts(sample.Fonts, sample.FontFaces),
            TopValues(sample.Radii.Select(r => (Value: NormaliseRadius(r.Value), r.Count)), MaxRadii, v => v is not ("0px" or "")),
            TopValues(sample.Shadows.Select(s => (Value: Shorten(CleanShadow(s.Value)), s.Count)), MaxShadows, v => v is not ("none" or "")),
            Spacing(sample.Spacing),
            CssAnalyzer.ExtractVariables(stylesheets),
            CssAnalyzer.ExtractBreakpoints(stylesheets),
            UtilityFrameworkDetector.Detect(sample.Sampled, sample.Classes));

        return FitToSize(design, TargetJsonBytes);
    }

    // Drops CSS variables from the end (but keeps at least MinVariablesKept) until the JSON form is
    // within maxBytes. Everything else is already bounded by the per-list limits.
    public static DesignSystem FitToSize(DesignSystem design, int maxBytes)
    {
        var current = design;
        while (current.CssVariables.Count > MinVariablesKept && SerialisedBytes(current) > maxBytes)
        {
            current = current with { CssVariables = [.. current.CssVariables.Take(current.CssVariables.Count - 1)] };
        }

        return current;
    }

    // The UTF-8 size of the summary as JSON (what it costs to store and send).
    public static int SerialisedBytes(DesignSystem design) =>
        System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(design, JsonOptions));

    // Merges colours that differ only slightly, counts uses per role, and guesses each role.
    internal static IReadOnlyList<ColorToken> Colors(IReadOnlyList<ColorSample> samples)
    {
        var clusters = new Dictionary<string, ColorCluster>(StringComparer.Ordinal);
        foreach (var sample in samples)
        {
            var hex = ColorParser.Normalize(sample.Value);
            if (hex is null || sample.Count <= 0)
            {
                continue;
            }

            var role = RoleIndex(sample.Role);
            if (role < 0)
            {
                continue;
            }

            var key = ColorParser.QuantizedKey(hex);
            if (!clusters.TryGetValue(key, out var cluster))
            {
                cluster = new ColorCluster(key);
                clusters[key] = cluster;
            }

            cluster.Add(hex, role, sample.Count);
        }

        var ordered = clusters.Values.OrderBy(c => c.Key, StringComparer.Ordinal).ToList();
        var topText = BestFor(ordered, ColorCluster.Text);
        var topBackground = BestFor(ordered, ColorCluster.Background);

        return ordered
            .OrderByDescending(c => c.Total)
            .ThenBy(c => c.Hex, StringComparer.Ordinal)
            .Take(MaxColors)
            .Select(c => new ColorToken(c.Hex, c.Total, GuessRole(c, c == topText, c == topBackground)))
            .ToList();
    }

    // Chooses a role: the most used text colour is "text", the most used background is "background",
    // a vivid colour used on links/buttons is "accent", otherwise the role it was used in most.
    private static string GuessRole(ColorCluster c, bool isTopText, bool isTopBackground)
    {
        if (isTopText)
        {
            return "text";
        }

        if (isTopBackground)
        {
            return "background";
        }

        if (c.ByRole[ColorCluster.Accent] > 0 && ColorParser.Saturation(c.Hex) >= AccentSaturation)
        {
            return "accent";
        }

        var text = c.ByRole[ColorCluster.Text];
        var background = c.ByRole[ColorCluster.Background];
        var border = c.ByRole[ColorCluster.Border];
        if (text >= background && text >= border)
        {
            return "text";
        }

        return background >= border ? "background" : "border";
    }

    // The cluster that used a role the most (ties: smaller key); null when nobody used it.
    private static ColorCluster? BestFor(List<ColorCluster> ordered, int role)
    {
        ColorCluster? best = null;
        foreach (var cluster in ordered)
        {
            if (cluster.ByRole[role] > 0 && (best is null || cluster.ByRole[role] > best.ByRole[role]))
            {
                best = cluster;
            }
        }

        return best;
    }

    // Index of a role name, or -1.
    private static int RoleIndex(string role) => role switch
    {
        "text" => ColorCluster.Text,
        "background" => ColorCluster.Background,
        "border" => ColorCluster.Border,
        "accent" => ColorCluster.Accent,
        _ => -1,
    };

    // Groups font usage by cleaned family name and keeps the top families with their weights and sizes.
    internal static IReadOnlyList<FontToken> Fonts(IReadOnlyList<FontSample> samples, IReadOnlyList<string>? loadedFaces = null)
    {
        var families = new Dictionary<string, FontUsage>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in samples)
        {
            var family = CleanFontFamily(sample.Family, loadedFaces);
            if (family is null || sample.Count <= 0)
            {
                continue;
            }

            if (!families.TryGetValue(family, out var usage))
            {
                usage = new FontUsage(family);
                families[family] = usage;
            }

            usage.Total += sample.Count;
            if (ParseWeight(sample.Weight) is { } weight)
            {
                usage.Weights[weight] = usage.Weights.GetValueOrDefault(weight) + sample.Count;
            }

            if (ParseSizePx(sample.Size) is { } size)
            {
                usage.Sizes[size] = usage.Sizes.GetValueOrDefault(size) + sample.Count;
            }
        }

        return families.Values
            .OrderByDescending(f => f.Total)
            .ThenBy(f => f.Family, StringComparer.Ordinal)
            .Take(MaxFonts)
            .Select(f => new FontToken(
                f.Family,
                [.. f.Weights.OrderByDescending(w => w.Value).ThenBy(w => w.Key).Take(MaxWeightsPerFont).Select(w => w.Key).Order()],
                [.. f.Sizes.OrderByDescending(s => s.Value).ThenBy(s => s.Key).Take(MaxSizesPerFont).Select(s => s.Key).Order()
                    .Select(s => s.ToString(CultureInfo.InvariantCulture) + "px")]))
            .ToList();
    }

    // The first real family in a font-family list: quotes removed, generic fallbacks skipped,
    // framework decoration removed ("__Inter_a1b2c3" and "__Inter_Fallback_a1b2c3" become "Inter",
    // underscores become spaces). When loadedFaces (the @font-face names the page loaded) holds a
    // face whose compact form (lower case, no spaces, underscores or hyphens) equals the name's, or
    // ends with / contains it (names of 6+ compact characters, and only when exactly one face
    // matches), that real name is used ("plexMono" -> "IBM Plex Mono"). Otherwise a single
    // all-lower-case word is capitalised ("inter" -> "Inter"). Returns null when the list holds only
    // generic names.
    public static string? CleanFontFamily(string? fontFamily, IReadOnlyList<string>? loadedFaces = null)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            return null;
        }

        foreach (var raw in SplitFamilies(fontFamily))
        {
            var name = raw.Trim().Trim('"', '\'').Trim();
            if (name.Length is 0 or > 60 || GenericFonts.Contains(name) || name.StartsWith("var(", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var cleaned = StripFrameworkDecoration(name);

            // Next.js adds a generated "<Name> Fallback" copy of every font; it is not a real family.
            if (cleaned.EndsWith(" Fallback", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[..^" Fallback".Length].TrimEnd();
            }

            return cleaned.Length == 0 ? null : MatchLoadedFace(cleaned, loadedFaces) ?? CapitaliseSingleWord(cleaned);
        }

        return null;
    }

    // "__Inter_a1b2c3" / "__Inter_Fallback_a1b2c3" -> "Inter"; other underscores become spaces.
    internal static string StripFrameworkDecoration(string name)
    {
        var match = FrameworkNameRegex().Match(name);
        var core = match.Success ? match.Groups["name"].Value : name;
        return core.Replace('_', ' ').Trim();
    }

    // The real @font-face name that the (already cleaned) name stands for, or null when none matches
    // clearly. An exact compact match wins; otherwise exactly one face may end with or contain it.
    private static string? MatchLoadedFace(string name, IReadOnlyList<string>? faces)
    {
        if (faces is null || faces.Count == 0)
        {
            return null;
        }

        var target = Compact(name);
        var candidates = faces
            .Select(StripFrameworkDecoration)
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Select(f => (Face: f, Key: Compact(f)))
            .ToList();

        var exact = candidates.Where(c => c.Key == target).Select(c => c.Face).Order(StringComparer.Ordinal).ToList();
        if (exact.Count > 0)
        {
            return exact[0];
        }

        if (target.Length < 6)
        {
            return null;
        }

        var partial = candidates.Where(c => c.Key.Contains(target, StringComparison.Ordinal)).Select(c => c.Face).ToList();
        return partial.Count == 1 ? partial[0] : null;
    }

    // Lower case with spaces, underscores and hyphens removed, for comparing family names.
    private static string Compact(string value) =>
        new(value.Where(ch => ch is not (' ' or '_' or '-')).Select(char.ToLowerInvariant).ToArray());

    // "inter" -> "Inter" (only a single all-lower-case word is changed).
    private static string CapitaliseSingleWord(string name) =>
        name.All(ch => ch is >= 'a' and <= 'z') ? char.ToUpperInvariant(name[0]) + name[1..] : name;

    [GeneratedRegex(@"^__(?<name>.+?)(?:_Fallback)?(?:_[0-9a-fA-F]{5,})?$", RegexOptions.CultureInvariant)]
    private static partial Regex FrameworkNameRegex();

    // Splits a font-family list on commas that are not inside quotes.
    private static IEnumerable<string> SplitFamilies(string list)
    {
        var start = 0;
        char quote = '\0';
        for (var i = 0; i < list.Length; i++)
        {
            var ch = list[i];
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }
            }
            else if (ch is '"' or '\'')
            {
                quote = ch;
            }
            else if (ch == ',')
            {
                yield return list[start..i];
                start = i + 1;
            }
        }

        yield return list[start..];
    }

    // "700" -> 700, "bold" -> 700, "normal" -> 400; anything else null.
    private static int? ParseWeight(string text)
    {
        var lower = text.Trim().ToLowerInvariant();
        if (lower == "normal")
        {
            return 400;
        }

        if (lower == "bold")
        {
            return 700;
        }

        return int.TryParse(lower, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight) && weight is >= 1 and <= 1000
            ? weight
            : null;
    }

    // "15.6px" -> 16; null for anything that is not a plain pixel size between 1 and 400.
    private static int? ParseSizePx(string text) => PixelsToInt(text, 400);

    // Reads "12px" / "12.4px" as a rounded integer within 1..max; null otherwise.
    private static int? PixelsToInt(string text, int max)
    {
        var trimmed = text.Trim();
        if (!trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase) ||
            !double.TryParse(trimmed[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value))
        {
            return null;
        }

        var rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return rounded >= 1 && rounded <= max ? rounded : null;
    }

    // The most frequent spacing steps (1..128 px), returned small to large as "8px".
    internal static IReadOnlyList<string> Spacing(IReadOnlyList<ValueSample> samples)
    {
        var counts = new Dictionary<int, int>();
        foreach (var sample in samples)
        {
            if (sample.Count > 0 &&
                int.TryParse(sample.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var px) &&
                px is >= 1 and <= MaxSpacingPx)
            {
                counts[px] = counts.GetValueOrDefault(px) + sample.Count;
            }
        }

        return counts
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key)
            .Take(MaxSpacing)
            .Select(p => p.Key)
            .Order()
            .Select(px => px.ToString(CultureInfo.InvariantCulture) + "px")
            .ToList();
    }

    // Merges equal values, keeps the most frequent ones that pass the filter (ties: ordinal value).
    internal static IReadOnlyList<string> TopValues(IEnumerable<(string Value, int Count)> values, int limit, Func<string, bool> keep)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (value, count) in values)
        {
            if (count > 0 && value.Length > 0 && keep(value))
            {
                counts[value] = counts.GetValueOrDefault(value) + count;
            }
        }

        return counts
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Take(limit)
            .Select(p => p.Key)
            .ToList();
    }

    // Collapses whitespace so equal values compare equal.
    private static string CleanValue(string value) => WhitespaceRegex().Replace(value, " ").Trim();

    // Tidies a border-radius: numbers lose exponents and extra decimals, anything above 999px (the
    // "fully round" 33554400px of rounded-full) becomes 9999px, and four equal corners collapse to one.
    internal static string NormaliseRadius(string value)
    {
        var text = NumberWithUnitRegex().Replace(CleanValue(value), m =>
        {
            if (!double.TryParse(m.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            {
                return m.Value;
            }

            var unit = m.Groups["u"].Value;
            if (unit is "" or "px" && number > 999)
            {
                return "9999px";
            }

            return number.ToString("0.##", CultureInfo.InvariantCulture) + unit;
        });

        var parts = text.Split(' ');
        return parts.Length > 1 && !text.Contains('/') && parts.Distinct().Count() == 1 ? parts[0] : text;
    }

    // Removes see-through placeholder layers ("rgba(0, 0, 0, 0) 0px 0px 0px 0px") from a box-shadow
    // list and returns the real layers; an empty string when nothing real is left.
    internal static string CleanShadow(string value)
    {
        var text = CleanValue(value);
        if (text == "none")
        {
            return text;
        }

        var kept = new List<string>();
        var start = 0;
        var depth = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] == '(')
            {
                depth++;
            }
            else if (i < text.Length && text[i] == ')')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (i == text.Length || (text[i] == ',' && depth == 0))
            {
                var layer = text[start..i].Trim();
                start = i + 1;
                if (layer.Length > 0 && !IsTransparentLayer(layer))
                {
                    kept.Add(layer);
                }
            }
        }

        return string.Join(", ", kept);
    }

    // True when the colour in one shadow layer is fully transparent.
    private static bool IsTransparentLayer(string layer)
    {
        var token = ShadowColorRegex().Match(layer);
        if (!token.Success)
        {
            return false;
        }

        var color = token.Value;
        return color.Equals("transparent", StringComparison.OrdinalIgnoreCase) ||
            (ModernColorAlphaZeroRegex().IsMatch(color)) ||
            ((color.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) || color.StartsWith("hsl", StringComparison.OrdinalIgnoreCase) || color[0] == '#') &&
                ColorParser.ParseRgb(color) is null);
    }

    [GeneratedRegex(@"(?<n>-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)(?<u>px|%|rem|em)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberWithUnitRegex();

    [GeneratedRegex(@"(?:rgba?|hsla?|oklab|oklch|lab|lch|color)\([^)]*\)|transparent|#[0-9a-fA-F]{3,8}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShadowColorRegex();

    [GeneratedRegex(@"/\s*0(?:\.0+)?%?\s*\)$", RegexOptions.CultureInvariant)]
    private static partial Regex ModernColorAlphaZeroRegex();

    // Cuts a long value (a box-shadow list) to the shadow length limit.
    private static string Shorten(string value) => value.Length > MaxShadowChars ? value[..MaxShadowChars] : value;

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    // Usage totals for one font family.
    private sealed class FontUsage(string family)
    {
        public string Family { get; } = family;

        public int Total { get; set; }

        public Dictionary<int, int> Weights { get; } = [];

        public Dictionary<int, int> Sizes { get; } = [];
    }

    // Colours that differ only by a tiny amount, merged. Hex is the most used original colour in the
    // group (ties: the smaller hex text), Total counts uses as text, background or border.
    private sealed class ColorCluster(string key)
    {
        public const int Text = 0;
        public const int Background = 1;
        public const int Border = 2;
        public const int Accent = 3;

        private readonly Dictionary<string, int> _originals = new(StringComparer.Ordinal);

        public string Key { get; } = key;

        public int[] ByRole { get; } = new int[4];

        public int Total => Math.Max(ByRole[Text] + ByRole[Background] + ByRole[Border], ByRole[Accent]);

        public string Hex => _originals
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .First().Key;

        public void Add(string hex, int role, int count)
        {
            _originals[hex] = _originals.GetValueOrDefault(hex) + count;
            ByRole[role] += count;
        }
    }
}
