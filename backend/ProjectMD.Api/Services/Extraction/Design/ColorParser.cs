using System.Globalization;

namespace ProjectMD.Api.Services.Extraction.Design;

// Turns any CSS colour text (hex, rgb(), rgba(), hsl(), hsla(), a colour name) into a lower-case
// "#rrggbb" string, and offers the small colour maths the design summary needs. Fully (or almost
// fully) transparent colours and anything it does not understand come back as null. A colour with
// partial transparency is kept as its plain red/green/blue value (the see-through part is ignored).
public static class ColorParser
{
    // Alpha at or below this counts as "not there at all" (anti-aliasing and ghost layers).
    private const double TransparentAlpha = 0.02;

    // The standard CSS colour names, as "name:rrggbb" pairs (built once into a lookup).
    private const string NamedColorData =
        "aliceblue:f0f8ff,antiquewhite:faebd7,aqua:00ffff,aquamarine:7fffd4,azure:f0ffff,beige:f5f5dc,bisque:ffe4c4," +
        "black:000000,blanchedalmond:ffebcd,blue:0000ff,blueviolet:8a2be2,brown:a52a2a,burlywood:deb887,cadetblue:5f9ea0," +
        "chartreuse:7fff00,chocolate:d2691e,coral:ff7f50,cornflowerblue:6495ed,cornsilk:fff8dc,crimson:dc143c,cyan:00ffff," +
        "darkblue:00008b,darkcyan:008b8b,darkgoldenrod:b8860b,darkgray:a9a9a9,darkgreen:006400,darkgrey:a9a9a9," +
        "darkkhaki:bdb76b,darkmagenta:8b008b,darkolivegreen:556b2f,darkorange:ff8c00,darkorchid:9932cc,darkred:8b0000," +
        "darksalmon:e9967a,darkseagreen:8fbc8f,darkslateblue:483d8b,darkslategray:2f4f4f,darkslategrey:2f4f4f," +
        "darkturquoise:00ced1,darkviolet:9400d3,deeppink:ff1493,deepskyblue:00bfff,dimgray:696969,dimgrey:696969," +
        "dodgerblue:1e90ff,firebrick:b22222,floralwhite:fffaf0,forestgreen:228b22,fuchsia:ff00ff,gainsboro:dcdcdc," +
        "ghostwhite:f8f8ff,gold:ffd700,goldenrod:daa520,gray:808080,green:008000,greenyellow:adff2f,grey:808080," +
        "honeydew:f0fff0,hotpink:ff69b4,indianred:cd5c5c,indigo:4b0082,ivory:fffff0,khaki:f0e68c,lavender:e6e6fa," +
        "lavenderblush:fff0f5,lawngreen:7cfc00,lemonchiffon:fffacd,lightblue:add8e6,lightcoral:f08080,lightcyan:e0ffff," +
        "lightgoldenrodyellow:fafad2,lightgray:d3d3d3,lightgreen:90ee90,lightgrey:d3d3d3,lightpink:ffb6c1," +
        "lightsalmon:ffa07a,lightseagreen:20b2aa,lightskyblue:87cefa,lightslategray:778899,lightslategrey:778899," +
        "lightsteelblue:b0c4de,lightyellow:ffffe0,lime:00ff00,limegreen:32cd32,linen:faf0e6,magenta:ff00ff," +
        "maroon:800000,mediumaquamarine:66cdaa,mediumblue:0000cd,mediumorchid:ba55d3,mediumpurple:9370db," +
        "mediumseagreen:3cb371,mediumslateblue:7b68ee,mediumspringgreen:00fa9a,mediumturquoise:48d1cc," +
        "mediumvioletred:c71585,midnightblue:191970,mintcream:f5fffa,mistyrose:ffe4e1,moccasin:ffe4b5," +
        "navajowhite:ffdead,navy:000080,oldlace:fdf5e6,olive:808000,olivedrab:6b8e23,orange:ffa500,orangered:ff4500," +
        "orchid:da70d6,palegoldenrod:eee8aa,palegreen:98fb98,paleturquoise:afeeee,palevioletred:db7093," +
        "papayawhip:ffefd5,peachpuff:ffdab9,peru:cd853f,pink:ffc0cb,plum:dda0dd,powderblue:b0e0e6,purple:800080," +
        "rebeccapurple:663399,red:ff0000,rosybrown:bc8f8f,royalblue:4169e1,saddlebrown:8b4513,salmon:fa8072," +
        "sandybrown:f4a460,seagreen:2e8b57,seashell:fff5ee,sienna:a0522d,silver:c0c0c0,skyblue:87ceeb," +
        "slateblue:6a5acd,slategray:708090,slategrey:708090,snow:fffafa,springgreen:00ff7f,steelblue:4682b4," +
        "tan:d2b48c,teal:008080,thistle:d8bfd8,tomato:ff6347,turquoise:40e0d0,violet:ee82ee,wheat:f5deb3," +
        "white:ffffff,whitesmoke:f5f5f5,yellow:ffff00,yellowgreen:9acd32";

    private static readonly Dictionary<string, string> NamedColors = BuildNamedColors();

    // Converts a CSS colour to "#rrggbb", or null when it is transparent or not understood.
    public static string? Normalize(string? css)
    {
        var rgb = ParseRgb(css);
        return rgb is null ? null : ToHex(rgb.Value);
    }

    // Reads a CSS colour into red/green/blue (0-255), or null (transparent or unknown).
    public static (int R, int G, int B)? ParseRgb(string? css)
    {
        if (string.IsNullOrWhiteSpace(css) || css.Length > 80)
        {
            return null;
        }

        var text = css.Trim().ToLowerInvariant();
        if (text == "transparent")
        {
            return null;
        }

        if (text[0] == '#')
        {
            return ParseHex(text[1..]);
        }

        var open = text.IndexOf('(');
        if (open > 0 && text.EndsWith(')'))
        {
            var name = text[..open].Trim();
            var args = text[(open + 1)..^1];
            return name switch
            {
                "rgb" or "rgba" => ParseRgbFunction(args),
                "hsl" or "hsla" => ParseHslFunction(args),
                _ => null,
            };
        }

        return NamedColors.TryGetValue(text, out var named) ? ParseHex(named) : null;
    }

    // Formats a colour as lower-case "#rrggbb".
    public static string ToHex((int R, int G, int B) c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    // Rounds every channel to the nearest multiple of 4 (255 stays 255), so shades that differ only
    // by anti-aliasing noise share one key. Returns the key as "#rrggbb"; it is only used to group.
    public static string QuantizedKey(string hex)
    {
        var rgb = ParseRgb(hex);
        if (rgb is null)
        {
            return hex;
        }

        static int Snap(int v) => Math.Min(255, (int)Math.Round(v / 4.0, MidpointRounding.AwayFromZero) * 4);
        return ToHex((Snap(rgb.Value.R), Snap(rgb.Value.G), Snap(rgb.Value.B)));
    }

    // How vivid a colour is, from 0 (grey) to 1 (fully saturated): (max - min) / max.
    public static double Saturation(string hex)
    {
        var rgb = ParseRgb(hex);
        if (rgb is null)
        {
            return 0;
        }

        var max = Math.Max(rgb.Value.R, Math.Max(rgb.Value.G, rgb.Value.B));
        var min = Math.Min(rgb.Value.R, Math.Min(rgb.Value.G, rgb.Value.B));
        return max == 0 ? 0 : (max - min) / (double)max;
    }

    // Reads "rgb" (3 digits), "rgba" (4), "rrggbb" (6) or "rrggbbaa" (8) hex digits.
    private static (int R, int G, int B)? ParseHex(string digits)
    {
        if (digits.Length is not (3 or 4 or 6 or 8) || !digits.All(Uri.IsHexDigit))
        {
            return null;
        }

        if (digits.Length <= 4)
        {
            digits = string.Concat(digits.Select(ch => new string(ch, 2)));
        }

        var r = int.Parse(digits.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = int.Parse(digits.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = int.Parse(digits.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (digits.Length == 8)
        {
            var a = int.Parse(digits.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            if (a <= TransparentAlpha)
            {
                return null;
            }
        }

        return (r, g, b);
    }

    // Reads the inside of rgb(...) / rgba(...): comma or space separated, numbers or percentages,
    // optional alpha (after a comma or a slash).
    private static (int R, int G, int B)? ParseRgbFunction(string args)
    {
        var parts = Split(args);
        if (parts.Length is not (3 or 4))
        {
            return null;
        }

        var channels = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (!TryNumber(parts[i], out var value, out var isPercent))
            {
                return null;
            }

            channels[i] = (int)Math.Round(Math.Clamp(isPercent ? value * 2.55 : value, 0, 255), MidpointRounding.AwayFromZero);
        }

        if (parts.Length == 4 && IsTransparent(parts[3]))
        {
            return null;
        }

        return (channels[0], channels[1], channels[2]);
    }

    // Reads the inside of hsl(...) / hsla(...) and converts it to red/green/blue.
    private static (int R, int G, int B)? ParseHslFunction(string args)
    {
        var parts = Split(args);
        if (parts.Length is not (3 or 4))
        {
            return null;
        }

        var hueText = parts[0].EndsWith("deg", StringComparison.Ordinal) ? parts[0][..^3] : parts[0];
        if (!TryNumber(hueText, out var hue, out _) ||
            !TryNumber(parts[1], out var saturation, out _) ||
            !TryNumber(parts[2], out var lightness, out _))
        {
            return null;
        }

        if (parts.Length == 4 && IsTransparent(parts[3]))
        {
            return null;
        }

        var h = (((hue % 360) + 360) % 360) / 360.0;
        var s = Math.Clamp(saturation / 100.0, 0, 1);
        var l = Math.Clamp(lightness / 100.0, 0, 1);
        var q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        var p = (2 * l) - q;

        return (ToChannel(HueToChannel(p, q, h + (1 / 3.0))), ToChannel(HueToChannel(p, q, h)), ToChannel(HueToChannel(p, q, h - (1 / 3.0))));
    }

    // One step of the standard HSL to RGB conversion.
    private static double HueToChannel(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }

        if (t > 1)
        {
            t -= 1;
        }

        return t switch
        {
            < 1 / 6.0 => p + ((q - p) * 6 * t),
            < 1 / 2.0 => q,
            < 2 / 3.0 => p + ((q - p) * ((2 / 3.0) - t) * 6),
            _ => p,
        };
    }

    // Scales 0..1 to 0..255.
    private static int ToChannel(double value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);

    // Splits function arguments on commas, spaces and the alpha slash.
    private static string[] Split(string args) =>
        args.Split([',', ' ', '/', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // True when an alpha part (0.5, 50%) is effectively zero.
    private static bool IsTransparent(string alpha) =>
        TryNumber(alpha, out var value, out var isPercent) && (isPercent ? value / 100.0 : value) <= TransparentAlpha;

    // Reads "12", "12.5" or "50%" (the percent sign is reported separately).
    private static bool TryNumber(string text, out double value, out bool isPercent)
    {
        isPercent = text.EndsWith('%');
        var number = isPercent ? text[..^1] : text;
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    // Builds the name lookup from the data string.
    private static Dictionary<string, string> BuildNamedColors()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in NamedColorData.Split(','))
        {
            var colon = pair.IndexOf(':');
            map[pair[..colon]] = pair[(colon + 1)..];
        }

        return map;
    }
}
