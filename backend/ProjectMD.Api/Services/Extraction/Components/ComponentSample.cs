using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectMD.Api.Services.Extraction.Components;

// Reads the answer of ComponentScript. The script runs inside a page we do not control, so everything
// is checked again here: known kinds and tags, known CSS property names, short plain values, and hard
// limits on the number of elements and the depth of the tree.
public static partial class ComponentSample
{
    public const int MaxItems = 8;
    private const int MaxNodes = 40;
    private const int MaxDepth = 5;
    private const int MaxChildren = 12;
    private const int MaxValueChars = 200;
    private const int MaxJsonChars = 400_000;

    // The kinds the script may report.
    public static readonly IReadOnlySet<string> Kinds =
        new HashSet<string>(StringComparer.Ordinal) { "button", "input", "card", "nav", "heading", "badge" };

    private static readonly HashSet<string> Tags = new(StringComparer.Ordinal)
    {
        "div", "span", "button", "a", "input", "label", "nav", "ul", "ol", "li", "p", "small", "strong", "em",
        "h1", "h2", "h3", "h4", "h5", "h6", "header", "section", "article", "textarea",
    };

    private static readonly HashSet<string> InputTypes = new(StringComparer.Ordinal)
    {
        "text", "email", "search", "password", "url", "tel",
    };

    // The CSS properties the script reads (anything else is dropped), plus width and height.
    public static readonly IReadOnlySet<string> Properties = BuildProperties();

    // Returns the parsed components, or null for anything that is not the expected shape or is empty.
    public static ComponentStyles? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonChars)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("items", out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var items = new List<ComponentSnapshot>();
            foreach (var entry in list.EnumerateArray())
            {
                if (items.Count >= MaxItems)
                {
                    break;
                }

                if (ReadSnapshot(entry) is { } snapshot)
                {
                    items.Add(snapshot);
                }
            }

            return items.Count == 0 ? null : new ComponentStyles(items);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ComponentSnapshot? ReadSnapshot(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object
            || Text(entry, "kind") is not { } kind || !Kinds.Contains(kind)
            || !entry.TryGetProperty("root", out var rootElement))
        {
            return null;
        }

        var budget = MaxNodes;
        var root = ReadNode(rootElement, 0, ref budget);
        if (root is null)
        {
            return null;
        }

        var background = Text(entry, "background") is { } b && HexColour().IsMatch(b) ? b.ToLowerInvariant() : "#ffffff";
        var height = entry.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number && h.TryGetInt32(out var px)
            ? Math.Clamp(px, 0, 2000)
            : 0;
        return new ComponentSnapshot(kind, background, height, root);
    }

    private static ComponentNode? ReadNode(JsonElement element, int depth, ref int budget)
    {
        if (budget <= 0 || depth > MaxDepth || element.ValueKind != JsonValueKind.Object
            || Text(element, "t") is not { } tag || !Tags.Contains(tag))
        {
            return null;
        }

        budget--;
        var styles = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.TryGetProperty("s", out var s) && s.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in s.EnumerateObject())
            {
                var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : string.Empty;
                if (Properties.Contains(property.Name) && value.Length is > 0 and <= MaxValueChars && !value.Any(char.IsControl))
                {
                    styles[property.Name] = value;
                }
            }
        }

        var children = new List<ComponentNode>();
        if (element.TryGetProperty("c", out var c) && c.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in c.EnumerateArray().Take(MaxChildren))
            {
                if (ReadNode(child, depth + 1, ref budget) is { } node)
                {
                    children.Add(node);
                }
            }
        }

        var type = Text(element, "i") is { } t && InputTypes.Contains(t) ? t : null;
        var hasText = element.TryGetProperty("x", out var x) && x.ValueKind == JsonValueKind.True;
        return new ComponentNode(tag, styles, type, hasText, children);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static HashSet<string> BuildProperties()
    {
        string[] sides = ["top", "right", "bottom", "left"];
        string[] corners = ["top-left", "top-right", "bottom-right", "bottom-left"];
        var set = new HashSet<string>(StringComparer.Ordinal)
        {
            "color", "background-color", "box-shadow", "font-family", "font-size", "font-weight", "font-style", "line-height",
            "letter-spacing", "text-transform", "text-align", "text-decoration-line", "display", "flex-direction", "flex-wrap",
            "align-items", "justify-content", "gap", "opacity", "cursor", "white-space", "width", "height",
        };
        foreach (var side in sides)
        {
            set.Add("border-" + side + "-width");
            set.Add("border-" + side + "-style");
            set.Add("border-" + side + "-color");
            set.Add("padding-" + side);
            set.Add("margin-" + side);
        }

        foreach (var corner in corners)
        {
            set.Add("border-" + corner + "-radius");
        }

        return set;
    }

    [GeneratedRegex(@"^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColour();
}
