using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectMD.Api.Services.Ai;

// What the vision model says about a page after looking at its screenshots and measured facts:
// a short summary of its motion, the animations it found (each with a written description), and a
// breakdown of the visual design (typography, colours and the components on the page).
public sealed record MotionNotes(string Summary, IReadOnlyList<DetectedAnimation> Animations, DesignBreakdown? Design = null)
{
    // True when the model said nothing usable.
    public bool IsEmpty => Summary.Length == 0 && Animations.Count == 0 && (Design is null || Design.IsEmpty);
}

// One detected animation. Section is where it is (Hero, Navigation...), Name a short title,
// Description a few sentences on what moves, how and when. Trigger is one of
// MotionNotesLimits.Triggers. Evidence is "measured" when it matches timing data the browser
// reported, "inferred" when it was read from the screenshots only. Library is the most likely
// library (may be empty) and Recreate a one-line hint for rebuilding it.
public sealed record DetectedAnimation(
    string Section,
    string Name,
    string Description,
    string Trigger,
    string Evidence,
    string Library,
    string Recreate);

// The AI's reading of the visual design: one overview paragraph, how each font is used, what each
// main colour is for, and the components (navigation bar, hero, buttons, cards...) with how they
// look.
public sealed record DesignBreakdown(
    string Overview,
    IReadOnlyList<TypographyNote> Typography,
    IReadOnlyList<ColorNote> Colors,
    IReadOnlyList<ComponentNote> Components)
{
    public bool IsEmpty => Overview.Length == 0 && Typography.Count == 0 && Colors.Count == 0 && Components.Count == 0;
}

// How one font is used: Role (Headings, Body text...), Font (the family), Details (sizes, weights,
// letter spacing, character).
public sealed record TypographyNote(string Role, string Font, string Details);

// One colour: Hex is "#rrggbb" or empty when the model gave no valid value; Name a plain name;
// Usage what it is used for.
public sealed record ColorNote(string Hex, string Name, string Usage);

// One component on the page: Name, Description (what it is and does), Styling (how it looks).
public sealed record ComponentNote(string Name, string Description, string Styling);

// Size limits, written into the prompt and enforced again when reading.
public static class MotionNotesLimits
{
    public const int SummaryChars = 400;
    public const int MaxAnimations = 12;
    public const int SectionChars = 60;
    public const int NameChars = 80;
    public const int DescriptionChars = 500;
    public const int LibraryChars = 60;
    public const int RecreateChars = 200;

    public const int DesignOverviewChars = 500;
    public const int MaxTypography = 6;
    public const int MaxColors = 10;
    public const int MaxComponents = 12;
    public const int ShortChars = 60;
    public const int DetailsChars = 200;
    public const int ComponentDescriptionChars = 300;

    // The trigger words the document understands; anything else becomes "unknown".
    public static readonly IReadOnlyList<string> Triggers =
        ["load", "scroll", "scroll-scrub", "hover", "click", "loop", "page-transition", "unknown"];
}

// The JSON shape of the answer. Sent as a strict schema when Motion:ResponseFormat is
// "json_schema", and written into the prompt in "json_object" mode. Every property is required and
// every object forbids extra properties (strict mode rules); allowed values and sizes are in the
// descriptions and enforced by MotionNotesParser.
public static class MotionNotesSchema
{
    public const string Name = "visual_notes";

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
                  "section": { "type": "string", "description": "Where on the page, for example Hero, Navigation, Work grid, Footer." },
                  "name": { "type": "string", "description": "A short title, for example Staggered headline reveal." },
                  "description": { "type": "string", "description": "2 to 4 sentences: what moves, how it moves (direction, fade, scale, speed, easing), and when. At most 500 characters." },
                  "trigger": { "type": "string", "description": "One of: load, scroll, scroll-scrub, hover, click, loop, page-transition, unknown." },
                  "evidence": { "type": "string", "description": "measured if it matches a measured animation or scroll trigger in the facts, otherwise inferred." },
                  "library": { "type": "string", "description": "The library that most likely runs it, from the facts (for example GSAP, CSS, Lenis), or empty." },
                  "recreate": { "type": "string", "description": "One short hint for rebuilding it, at most 200 characters." }
                },
                "required": ["section", "name", "description", "trigger", "evidence", "library", "recreate"],
                "additionalProperties": false
              }
            },
            "design": {
              "type": "object",
              "properties": {
                "overview": { "type": "string", "description": "The overall visual style in 2 to 3 sentences, at most 500 characters." },
                "typography": {
                  "type": "array",
                  "description": "At most 6 entries, one per font role.",
                  "items": {
                    "type": "object",
                    "properties": {
                      "role": { "type": "string", "description": "For example Headings, Body text, Navigation, Buttons." },
                      "font": { "type": "string", "description": "The font family, from the facts when listed." },
                      "details": { "type": "string", "description": "Sizes, weights, case, spacing and character, at most 200 characters." }
                    },
                    "required": ["role", "font", "details"],
                    "additionalProperties": false
                  }
                },
                "colors": {
                  "type": "array",
                  "description": "At most 10 main colours.",
                  "items": {
                    "type": "object",
                    "properties": {
                      "hex": { "type": "string", "description": "#rrggbb, from the facts when listed." },
                      "name": { "type": "string", "description": "A plain colour name." },
                      "usage": { "type": "string", "description": "What it is used for, at most 200 characters." }
                    },
                    "required": ["hex", "name", "usage"],
                    "additionalProperties": false
                  }
                },
                "components": {
                  "type": "array",
                  "description": "At most 12 visible components, top of the page first.",
                  "items": {
                    "type": "object",
                    "properties": {
                      "name": { "type": "string", "description": "For example Navigation bar, Hero, Primary button, Project card, Footer." },
                      "description": { "type": "string", "description": "What it is and what it contains, at most 300 characters." },
                      "styling": { "type": "string", "description": "How it looks: layout, colours, corners, borders, shadows, spacing, at most 200 characters." }
                    },
                    "required": ["name", "description", "styling"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["overview", "typography", "colors", "components"],
              "additionalProperties": false
            }
          },
          "required": ["summary", "animations", "design"],
          "additionalProperties": false
        }
        """;
}

// Reads and writes the notes. Reading never trusts the model: every string is sanitised and cut,
// unknown trigger and evidence words are replaced with safe ones, lists are capped, and a part with
// the wrong shape is simply left out (the answer may come from json_object mode, where the shape is
// not enforced by the provider).
public static partial class MotionNotesParser
{
    [GeneratedRegex(@"^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexRegex();

    // Turns the model's answer (or a cached copy) into safe notes; null when empty or not JSON.
    public static MotionNotes? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(StripFences(json), new JsonDocumentOptions { MaxDepth = 10 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var summary = Text(root, "summary", MotionNotesLimits.SummaryChars);
            var animations = Items(root, "animations", MotionNotesLimits.MaxAnimations, ReadAnimation);
            var design = root.TryGetProperty("design", out var d) && d.ValueKind == JsonValueKind.Object ? ReadDesign(d) : null;

            var notes = new MotionNotes(summary, animations, design is { IsEmpty: false } ? design : null);
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
                name = a.Name,
                description = a.Description,
                trigger = a.Trigger,
                evidence = a.Evidence,
                library = a.Library,
                recreate = a.Recreate,
            }),
            design = notes.Design is null ? null : new
            {
                overview = notes.Design.Overview,
                typography = notes.Design.Typography.Select(t => new { role = t.Role, font = t.Font, details = t.Details }),
                colors = notes.Design.Colors.Select(c => new { hex = c.Hex, name = c.Name, usage = c.Usage }),
                components = notes.Design.Components.Select(c => new { name = c.Name, description = c.Description, styling = c.Styling }),
            },
        });

    // One animation; null when it is not an object or has neither a name nor a description.
    private static DetectedAnimation? ReadAnimation(JsonElement item)
    {
        var name = Text(item, "name", MotionNotesLimits.NameChars);
        var description = Text(item, "description", MotionNotesLimits.DescriptionChars);
        if (name.Length == 0 && description.Length == 0)
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
            name,
            description,
            trigger,
            evidence,
            Text(item, "library", MotionNotesLimits.LibraryChars),
            Text(item, "recreate", MotionNotesLimits.RecreateChars));
    }

    // The design part.
    private static DesignBreakdown ReadDesign(JsonElement design) => new(
        Text(design, "overview", MotionNotesLimits.DesignOverviewChars),
        Items(design, "typography", MotionNotesLimits.MaxTypography, item =>
        {
            var role = Text(item, "role", MotionNotesLimits.ShortChars);
            var font = Text(item, "font", MotionNotesLimits.ShortChars);
            return role.Length == 0 && font.Length == 0
                ? null
                : new TypographyNote(role, font, Text(item, "details", MotionNotesLimits.DetailsChars));
        }),
        Items(design, "colors", MotionNotesLimits.MaxColors, item =>
        {
            var hex = Text(item, "hex", 20);
            hex = HexRegex().IsMatch(hex) ? hex.ToLowerInvariant() : string.Empty;
            var name = Text(item, "name", MotionNotesLimits.ShortChars);
            var usage = Text(item, "usage", MotionNotesLimits.DetailsChars);
            return hex.Length == 0 && name.Length == 0 ? null : new ColorNote(hex, name, usage);
        }),
        Items(design, "components", MotionNotesLimits.MaxComponents, item =>
        {
            var name = Text(item, "name", MotionNotesLimits.ShortChars);
            return name.Length == 0
                ? null
                : new ComponentNote(
                    name,
                    Text(item, "description", MotionNotesLimits.ComponentDescriptionChars),
                    Text(item, "styling", MotionNotesLimits.DetailsChars));
        }));

    // Reads up to `max` usable objects from an array property; anything else is skipped.
    private static IReadOnlyList<T> Items<T>(JsonElement parent, string name, int max, Func<JsonElement, T?> read)
        where T : class
    {
        var list = new List<T>();
        if (!parent.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (list.Count >= max)
            {
                break;
            }

            if (item.ValueKind == JsonValueKind.Object && read(item) is { } value)
            {
                list.Add(value);
            }
        }

        return list;
    }

    // A cleaned string property, or empty when missing or not a string.
    private static string Text(JsonElement item, string name, int maxChars) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? AiProseSanitizer.Clean(value.GetString(), maxChars)
            : string.Empty;

    // Removes a ```json ... ``` wrapper if a model added one anyway.
    private static string StripFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstLine = trimmed.IndexOf('\n');
        var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstLine > 0 && end > firstLine ? trimmed[(firstLine + 1)..end].Trim() : trimmed;
    }
}
