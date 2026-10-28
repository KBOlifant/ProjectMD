using System.Text.Json;
using ProjectMD.Api.Services.Extraction.Components;

namespace ProjectMD.Api.Services.Ai;

// Reads the component previews from the model's JSON answer (or from the cache) and cleans every
// one with ComponentSanitizer. Anything damaged gives null; a bad entry is skipped.
public static class ComponentPreviewsParser
{
    public const int MaxComponents = 16;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    // Accepts {"components":[...]} (the model) or {"items":[...]} (what we cache and store).
    public static ComponentPreviews? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 200_000)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var list = root.TryGetProperty("components", out var c) ? c
                : root.TryGetProperty("items", out var i) ? i
                : default;
            if (list.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var items = new List<ComponentPreview>();
            foreach (var entry in list.EnumerateArray())
            {
                if (items.Count >= MaxComponents)
                {
                    break;
                }

                if (entry.ValueKind == JsonValueKind.Object
                    && ComponentSanitizer.Clean(Text(entry, "kind"), Text(entry, "name"), Text(entry, "html"), Text(entry, "css"), Text(entry, "background")) is { } preview)
                {
                    items.Add(preview);
                }
            }

            return items.Count == 0 ? null : new ComponentPreviews(items);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The JSON kept in the cache.
    public static string ToJson(ComponentPreviews previews) => JsonSerializer.Serialize(previews, Options);

    private static string? Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
