using System.Text.Json;

namespace ProjectMD.Api.Services.Ai;

// What the AI contributes to a scan document: a few short, human-written explanations. Facts
// (technologies, endpoints, colours...) are rendered by code, never written by the model.
// Endpoints carry only an id that the prompt handed out (E1, E2, ...) and a purpose sentence.
public sealed record AiDocument(
    string Overview,
    IReadOnlyList<string> Architecture,
    IReadOnlyList<AiEndpointNote> Endpoints,
    IReadOnlyList<string> Design,
    IReadOnlyList<string> Tips)
{
    // True when the model said nothing usable at all.
    public bool IsEmpty =>
        Overview.Length == 0 && Architecture.Count == 0 && Endpoints.Count == 0 && Design.Count == 0 && Tips.Count == 0;
}

// What one API call is for, tied to the call by the id the prompt gave it.
public sealed record AiEndpointNote(string Id, string Purpose);

// The hard size limits of the AI document. They are written into the prompt and the schema
// descriptions, and enforced again (by cutting, never by failing) when the answer is read.
public static class AiDocumentLimits
{
    public const int OverviewChars = 400;
    public const int ArchitectureItems = 6;
    public const int ArchitectureChars = 140;
    public const int EndpointPurposeChars = 100;
    public const int DesignItems = 5;
    public const int DesignChars = 120;
    public const int TipsItems = 5;
    public const int TipsChars = 140;

    // How many endpoints get an id (E1..E15) in the prompt.
    public const int MaxPromptEndpoints = 15;
}

// The JSON schema sent to Groq as a strict structured-output request. It is written for Groq's
// strict mode: every property is required and every object forbids extra properties. Groq's docs do
// not list maxLength / maxItems as supported, so the size limits are given in the descriptions and
// enforced by AiDocumentParser instead.
public static class AiDocumentSchema
{
    public const string Json = """
        {
          "type": "object",
          "properties": {
            "overview": { "type": "string", "description": "What the site is and who it is for, at most 400 characters." },
            "architecture": {
              "type": "array",
              "description": "At most 6 bullets of at most 140 characters on how the site is built.",
              "items": { "type": "string" }
            },
            "endpoints": {
              "type": "array",
              "description": "One entry per listed API call id, purpose at most 100 characters.",
              "items": {
                "type": "object",
                "properties": {
                  "id": { "type": "string", "description": "An id from the list, for example E1." },
                  "purpose": { "type": "string", "description": "What this call is for." }
                },
                "required": ["id", "purpose"],
                "additionalProperties": false
              }
            },
            "design": {
              "type": "array",
              "description": "At most 5 bullets of at most 120 characters on the visual design system.",
              "items": { "type": "string" }
            },
            "tips": {
              "type": "array",
              "description": "At most 5 bullets of at most 140 characters of practical notes for a developer.",
              "items": { "type": "string" }
            }
          },
          "required": ["overview", "architecture", "endpoints", "design", "tips"],
          "additionalProperties": false
        }
        """;
}

// Reads and writes the AI document. Reading never trusts the model: wrong shapes are rejected,
// every string is sanitised, sizes are cut to the limits, unknown or repeated endpoint ids are
// dropped.
public static class AiDocumentParser
{
    // Turns the model's answer (or a cached copy) into a safe document. Returns null when the text
    // is empty, is not a JSON object of the expected shape, or holds nothing usable. `knownIds` are
    // the endpoint ids the prompt handed out.
    public static AiDocument? Parse(string? json, IReadOnlySet<string> knownIds)
    {
        if (string.IsNullOrWhiteSpace(json))
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

            if (!TryString(root, "overview", AiDocumentLimits.OverviewChars, out var overview)
                || !TryStrings(root, "architecture", AiDocumentLimits.ArchitectureItems, AiDocumentLimits.ArchitectureChars, out var architecture)
                || !TryEndpoints(root, knownIds, out var endpoints)
                || !TryStrings(root, "design", AiDocumentLimits.DesignItems, AiDocumentLimits.DesignChars, out var design)
                || !TryStrings(root, "tips", AiDocumentLimits.TipsItems, AiDocumentLimits.TipsChars, out var tips))
            {
                return null;
            }

            var result = new AiDocument(overview, architecture, endpoints, design, tips);
            return result.IsEmpty ? null : result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The canonical JSON stored in the cache (already validated and sanitised).
    public static string ToJson(AiDocument document) =>
        JsonSerializer.Serialize(new
        {
            overview = document.Overview,
            architecture = document.Architecture,
            endpoints = document.Endpoints.Select(e => new { id = e.Id, purpose = e.Purpose }),
            design = document.Design,
            tips = document.Tips,
        });

    // A missing property counts as empty; a property of the wrong type rejects the whole answer.
    private static bool TryString(JsonElement root, string name, int maxChars, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = AiProseSanitizer.Clean(element.GetString(), maxChars);
        return true;
    }

    // A list of short strings: at most `maxItems` non-empty items, each cut to `maxChars`.
    private static bool TryStrings(JsonElement root, string name, int maxItems, int maxChars, out IReadOnlyList<string> values)
    {
        var list = new List<string>();
        values = list;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var line = AiProseSanitizer.Clean(item.GetString(), maxChars);
            if (line.Length > 0 && list.Count < maxItems)
            {
                list.Add(line);
            }
        }

        return true;
    }

    // The endpoint notes: unknown ids are ignored and the first note for an id wins.
    private static bool TryEndpoints(JsonElement root, IReadOnlySet<string> knownIds, out IReadOnlyList<AiEndpointNote> notes)
    {
        var list = new List<AiEndpointNote>();
        notes = list;
        if (!root.TryGetProperty("endpoints", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("purpose", out var purposeElement) || purposeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var id = idElement.GetString()?.Trim() ?? string.Empty;
            if (!knownIds.Contains(id) || !seen.Add(id))
            {
                continue;
            }

            var purpose = AiProseSanitizer.Clean(purposeElement.GetString(), AiDocumentLimits.EndpointPurposeChars);
            if (purpose.Length > 0)
            {
                list.Add(new AiEndpointNote(id, purpose));
            }
        }

        return true;
    }
}

