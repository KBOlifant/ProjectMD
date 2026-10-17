using System.Text.Json;

namespace ProjectMD.Api.Services.Ai;

// What the vision model says about motion on a page: one short summary and a list of the
// animations it detected. Each animation says WHERE it is (section), WHAT it does (effect), WHEN it
// runs (trigger), WHAT it is based on (evidence: "measured" when it matches timing data the browser
// reported, "inferred" when it is read from the screenshots only), the library if known, and a
// one-line hint for rebuilding it.
public sealed record MotionNotes(string Summary, IReadOnlyList<DetectedAnimation> Animations)
{
    // True when the model said nothing usable.
    public bool IsEmpty => Summary.Length == 0 && Animations.Count == 0;
}

// One detected animation (see MotionNotes).
public sealed record DetectedAnimation(
    string Section,
    string Effect,
    string Trigger,
    string Evidence,
    string Library,
    string Recreate);

// Size limits of the motion notes, written into the prompt and enforced again when reading.
public static class MotionNotesLimits
{
    public const int SummaryChars = 400;
    public const int MaxAnimations = 12;
    public const int SectionChars = 60;
    public const int EffectChars = 200;
    public const int LibraryChars = 60;
    public const int RecreateChars = 200;

    // The trigger words the document understands; anything else becomes "unknown".
    public static readonly IReadOnlyList<string> Triggers =
        ["load", "scroll", "scroll-scrub", "hover", "click", "loop", "page-transition", "unknown"];
}

// The JSON schema for Groq's strict structured output. Every property is required and every object
// forbids extra properties (strict mode rules). Allowed values and sizes are given in descriptions
// and enforced by MotionNotesParser, because Groq's docs do not promise support for enum,
// maxLength or maxItems.
public static class MotionNotesSchema
{
    public const string Name = "motion_notes";

    public const string Json = """
        {
          "type": "object",
          "properties": {
            "summary": { "type": "string", "description": "How the page uses motion overall, at most 400 characters." },
            "animations": {
              "type": "array",
              "description": "At most 12 animations, most noticeable first.",
              "items": {
                "type": "object",
                "properties": {
                  "section": { "type": "string", "description": "Where on the page, for example Hero, Navigation, Work grid, Footer. At most 60 characters." },
                  "effect": { "type": "string", "description": "What moves and how, for example: headline letters slide up one after another. At most 200 characters." },
                  "trigger": { "type": "string", "description": "One of: load, scroll, scroll-scrub, hover, click, loop, page-transition, unknown." },
                  "evidence": { "type": "string", "description": "measured if it matches a measured animation or scroll trigger in the facts, otherwise inferred." },
                  "library": { "type": "string", "description": "The library that most likely runs it, from the facts (for example GSAP, CSS, Lenis), or empty." },
                  "recreate": { "type": "string", "description": "One short hint for rebuilding it, at most 200 characters." }
                },
                "required": ["section", "effect", "trigger", "evidence", "library", "recreate"],
                "additionalProperties": false
              }
            }
          },
          "required": ["summary", "animations"],
          "additionalProperties": false
        }
        """;
}

// Reads and writes motion notes. Reading never trusts the model: wrong shapes are rejected, every
// string is sanitised and cut, unknown trigger and evidence words are replaced with safe ones, and
// the list is capped.
public static class MotionNotesParser
{
    // Turns the model's answer (or a cached copy) into safe notes; null when empty or malformed.
    public static MotionNotes? Parse(string? json)
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

            var summary = string.Empty;
            if (root.TryGetProperty("summary", out var s))
            {
                if (s.ValueKind == JsonValueKind.String)
                {
                    summary = AiProseSanitizer.Clean(s.GetString(), MotionNotesLimits.SummaryChars);
                }
                else if (s.ValueKind != JsonValueKind.Null)
                {
                    return null;
                }
            }

            var animations = new List<DetectedAnimation>();
            if (root.TryGetProperty("animations", out var list) && list.ValueKind != JsonValueKind.Null)
            {
                if (list.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                foreach (var item in list.EnumerateArray())
                {
                    if (animations.Count >= MotionNotesLimits.MaxAnimations)
                    {
                        break;
                    }

                    if (ReadAnimation(item) is { } animation)
                    {
                        animations.Add(animation);
                    }
                }
            }

            var notes = new MotionNotes(summary, animations);
            return notes.IsEmpty ? null : notes;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The canonical JSON stored in the cache (already validated and sanitised).
    public static string ToJson(MotionNotes notes) =>
        JsonSerializer.Serialize(new
        {
            summary = notes.Summary,
            animations = notes.Animations.Select(a => new
            {
                section = a.Section,
                effect = a.Effect,
                trigger = a.Trigger,
                evidence = a.Evidence,
                library = a.Library,
                recreate = a.Recreate,
            }),
        });

    // One animation; null when it is not an object or has no effect text.
    private static DetectedAnimation? ReadAnimation(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var effect = Text(item, "effect", MotionNotesLimits.EffectChars);
        if (effect.Length == 0)
        {
            return null;
        }

        var trigger = Text(item, "trigger", 30).ToLowerInvariant();
        if (!MotionNotesLimits.Triggers.Contains(trigger))
        {
            trigger = "unknown";
        }

        var evidence = Text(item, "evidence", 20).ToLowerInvariant() == "measured" ? "measured" : "inferred";

        return new DetectedAnimation(
            Text(item, "section", MotionNotesLimits.SectionChars),
            effect,
            trigger,
            evidence,
            Text(item, "library", MotionNotesLimits.LibraryChars),
            Text(item, "recreate", MotionNotesLimits.RecreateChars));
    }

    // A cleaned string property, or empty when missing or not a string.
    private static string Text(JsonElement item, string name, int maxChars) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? AiProseSanitizer.Clean(value.GetString(), maxChars)
            : string.Empty;
}
