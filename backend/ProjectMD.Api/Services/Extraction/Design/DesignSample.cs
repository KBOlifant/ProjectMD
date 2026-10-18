using System.Text.Json;

namespace ProjectMD.Api.Services.Extraction.Design;

// A colour value seen in the page, where it was used ("text", "background", "border" or "accent"),
// and by how many sampled elements. Value is the browser's own text, such as "rgb(17, 24, 39)".
public sealed record ColorSample(string Role, string Value, int Count);

// A font seen in the page: the font-family list as written, the weight, the size (for example
// "16px") and how many sampled elements used that combination.
public sealed record FontSample(string Family, string Weight, string Size, int Count);

// Any other value (a radius, a shadow, a spacing number) with how many times it was seen.
public sealed record ValueSample(string Value, int Count);

// The set of class names one sampled element carries (only elements with at least 3 classes are
// reported), with how many sampled elements had exactly that set.
public sealed record ClassSample(IReadOnlyList<string> Classes, int Count);

// The raw, un-merged answer of the in-page script: counts of style values from up to 800 visible
// elements. It holds styles only, never text or form values. DesignAnalyzer turns it into the
// compact DesignSystem. Every list is already limited by the script, and TryParse limits them again
// because the script runs inside a page we do not control.
public sealed record DesignSample(
    int Sampled,
    IReadOnlyList<ColorSample> Colors,
    IReadOnlyList<FontSample> Fonts,
    IReadOnlyList<ValueSample> Radii,
    IReadOnlyList<ValueSample> Shadows,
    IReadOnlyList<ValueSample> Spacing,
    IReadOnlyList<ClassSample> Classes,
    IReadOnlyList<string>? FontFaces = null)
{
    // The most entries read from any one list, whatever the page sent.
    private const int MaxEntries = 400;

    // The longest single value read (a box-shadow list, a font stack).
    private const int MaxValueChars = 300;

    // Reads the script's JSON answer. Returns false for anything that is not the expected shape;
    // odd or oversized entries are skipped rather than failing the whole read.
    public static bool TryParse(string? json, out DesignSample sample)
    {
        sample = new DesignSample(0, [], [], [], [], [], []);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var sampled = root.TryGetProperty("sampled", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out var n)
                ? Math.Clamp(n, 0, 100_000)
                : 0;

            sample = new DesignSample(
                sampled,
                Rows(root, "colors", 3, row => new ColorSample(Text(row[0]), Text(row[1]), Number(row[2]))),
                Rows(root, "fonts", 4, row => new FontSample(Text(row[0]), Text(row[1]), Text(row[2]), Number(row[3]))),
                Rows(root, "radii", 2, row => new ValueSample(Text(row[0]), Number(row[1]))),
                Rows(root, "shadows", 2, row => new ValueSample(Text(row[0]), Number(row[1]))),
                Rows(root, "spacing", 2, row => new ValueSample(Text(row[0]), Number(row[1]))),
                Rows(root, "classes", 2, row => new ClassSample(
                    Text(row[0]).Split(' ', StringSplitOptions.RemoveEmptyEntries), Number(row[1]))),
                Faces(root));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Reads the "faces" property: names of the font faces the page loaded (strings only).
    private static List<string> Faces(JsonElement root)
    {
        var result = new List<string>();
        if (root.TryGetProperty("faces", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray().Take(100))
            {
                var name = Text(item).Trim();
                if (name.Length > 0 && name.Length <= 80)
                {
                    result.Add(name);
                }
            }
        }

        return result;
    }

    // Reads one property that is an array of fixed-length arrays; rows of the wrong shape are skipped.
    private static List<T> Rows<T>(JsonElement root, string name, int width, Func<JsonElement[], T> make)
    {
        var result = new List<T>();
        if (!root.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in list.EnumerateArray())
        {
            if (result.Count >= MaxEntries)
            {
                break;
            }

            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != width)
            {
                continue;
            }

            result.Add(make([.. item.EnumerateArray()]));
        }

        return result;
    }

    // A string cell, cut to a sane length (non-strings become empty).
    private static string Text(JsonElement cell)
    {
        if (cell.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        var text = cell.GetString() ?? string.Empty;
        return text.Length > MaxValueChars ? text[..MaxValueChars] : text;
    }

    // A count cell, kept in a small positive range (non-numbers become 0).
    private static int Number(JsonElement cell) =>
        cell.ValueKind == JsonValueKind.Number && cell.TryGetInt32(out var value) ? Math.Clamp(value, 0, 1_000_000) : 0;
}
