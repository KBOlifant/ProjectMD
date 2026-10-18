using System.Text.Json;

namespace ProjectMD.Api.Services.Extraction.Components;

// Reads the answer of ComponentScript. The script runs inside a page we do not control, so
// everything is checked again here: known kinds, known CSS property names, short plain values.
public static class ComponentSample
{
    public const int MaxItems = 12;
    private const int MaxValueChars = 200;

    // The kinds the script may report.
    public static readonly IReadOnlySet<string> Kinds =
        new HashSet<string>(StringComparer.Ordinal) { "button", "input", "card", "nav", "heading", "badge" };

    // The CSS properties the script reads (anything else is dropped).
    public static readonly IReadOnlySet<string> Properties = new HashSet<string>(StringComparer.Ordinal)
    {
        "color", "background-color", "border-top-width", "border-top-style", "border-top-color", "border-radius",
        "box-shadow", "font-family", "font-size", "font-weight", "line-height", "letter-spacing", "text-transform",
        "padding-top", "padding-right", "padding-bottom", "padding-left", "width", "height", "display", "gap",
        "text-decoration-line",
    };

    // Returns the parsed styles, or null for anything that is not the expected shape or is empty.
    public static ComponentStyles? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 100_000)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("items", out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var items = new List<ComponentStyle>();
            foreach (var entry in list.EnumerateArray())
            {
                if (items.Count >= MaxItems)
                {
                    break;
                }

                var item = ReadItem(entry);
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            return items.Count == 0 ? null : new ComponentStyles(items);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ComponentStyle? ReadItem(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object
            || !entry.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String
            || !entry.TryGetProperty("styles", out var styles) || styles.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var kindText = kind.GetString() ?? string.Empty;
        if (!Kinds.Contains(kindText))
        {
            return null;
        }

        var tag = entry.TryGetProperty("tag", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? string.Empty : string.Empty;
        if (tag.Length is 0 or > 12 || !tag.All(char.IsAsciiLetterOrDigit))
        {
            return null;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in styles.EnumerateObject())
        {
            var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : string.Empty;
            if (Properties.Contains(property.Name) && value.Length is > 0 and <= MaxValueChars && !value.Any(char.IsControl))
            {
                map[property.Name] = value;
            }
        }

        return map.Count == 0 ? null : new ComponentStyle(kindText, tag.ToLowerInvariant(), map);
    }
}
