using System.Text.Json;
using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Pipeline;

namespace ProjectMD.Api.Tests.Ai;

// Tests for the AI document: the schema, reading the model's answer safely (shape, limits, ids,
// hostile text) and the sanitiser. No network and no model.
public class AiDocumentTests
{
    private static readonly IReadOnlySet<string> Ids = new HashSet<string> { "E1", "E2", "E3" };

    // A hostile string full of everything that could change a document's structure.
    internal const string Hostile =
        "# Heading\n- list <script>alert(1)</script> [click](javascript:alert(1)) ![img](http://evil/x.png) "
        + "| cell | `code` ``` **bold** https://evil.example/x www.evil.example a@b.c <img src=x onerror=y>";

    // The schema is valid JSON written for strict mode: everything required, nothing extra allowed.
    [Fact]
    public void Schema_is_valid_strict_json()
    {
        using var doc = JsonDocument.Parse(AiDocumentSchema.Json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        var required = root.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        var properties = root.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(properties.OrderBy(x => x), required.OrderBy(x => x));
        Assert.Equal(["architecture", "design", "endpoints", "overview", "tips"], properties.OrderBy(x => x));

        var endpointItem = root.GetProperty("properties").GetProperty("endpoints").GetProperty("items");
        Assert.False(endpointItem.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["id", "purpose"], endpointItem.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList());
    }

    // A good answer comes through with ids mapped and unknown or repeated ids dropped.
    [Fact]
    public void Endpoint_ids_are_mapped_and_unknown_or_duplicate_ids_dropped()
    {
        var json = JsonSerializer.Serialize(new
        {
            overview = "Acme.",
            architecture = Array.Empty<string>(),
            endpoints = new[]
            {
                new { id = "E2", purpose = "second" },
                new { id = "E99", purpose = "invented" },
                new { id = "E2", purpose = "duplicate" },
                new { id = "E1", purpose = "first" },
            },
            design = Array.Empty<string>(),
            tips = Array.Empty<string>(),
        });

        var doc = AiDocumentParser.Parse(json, Ids)!;

        Assert.Equal([new AiEndpointNote("E2", "second"), new AiEndpointNote("E1", "first")], doc.Endpoints);
    }

    // Oversized input is cut to the limits instead of failing the document.
    [Fact]
    public void Limits_are_enforced_by_truncation()
    {
        var json = JsonSerializer.Serialize(new
        {
            overview = new string('o', 5000),
            architecture = Enumerable.Repeat(new string('a', 500), 20).ToArray(),
            endpoints = new[] { new { id = "E1", purpose = new string('p', 900) } },
            design = Enumerable.Repeat(new string('d', 500), 20).ToArray(),
            tips = Enumerable.Repeat(new string('t', 500), 20).ToArray(),
        });

        var doc = AiDocumentParser.Parse(json, Ids)!;

        Assert.Equal(AiDocumentLimits.OverviewChars, doc.Overview.Length);
        Assert.Equal(AiDocumentLimits.ArchitectureItems, doc.Architecture.Count);
        Assert.All(doc.Architecture, s => Assert.Equal(AiDocumentLimits.ArchitectureChars, s.Length));
        Assert.Equal(AiDocumentLimits.EndpointPurposeChars, doc.Endpoints[0].Purpose.Length);
        Assert.Equal(AiDocumentLimits.DesignItems, doc.Design.Count);
        Assert.All(doc.Design, s => Assert.Equal(AiDocumentLimits.DesignChars, s.Length));
        Assert.Equal(AiDocumentLimits.TipsItems, doc.Tips.Count);
        Assert.All(doc.Tips, s => Assert.Equal(AiDocumentLimits.TipsChars, s.Length));
    }

    // Bad answers give null, which means "use the basic document".
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"overview\": \"cut off")]
    [InlineData("[]")]
    [InlineData("\"just a string\"")]
    [InlineData("{}")]
    [InlineData("{\"overview\":\"\",\"architecture\":[],\"endpoints\":[],\"design\":[],\"tips\":[]}")]
    [InlineData("{\"overview\": 5}")]
    [InlineData("{\"overview\":\"x\",\"architecture\":\"not an array\"}")]
    [InlineData("{\"overview\":\"x\",\"architecture\":[1,2]}")]
    [InlineData("{\"overview\":\"x\",\"endpoints\":[\"E1\"]}")]
    [InlineData("{\"overview\":\"x\",\"endpoints\":[{\"id\":\"E1\"}]}")]
    public void Unusable_answers_give_null(string? json)
    {
        Assert.Null(AiDocumentParser.Parse(json, Ids));
    }

    // Deeply nested garbage is rejected, not crashed on.
    [Fact]
    public void Deeply_nested_json_is_rejected()
    {
        var json = "{\"overview\":" + string.Concat(Enumerable.Repeat("[", 200)) + string.Concat(Enumerable.Repeat("]", 200)) + "}";

        Assert.Null(AiDocumentParser.Parse(json, Ids));
    }

    // The cached JSON round-trips to the same document.
    [Fact]
    public void Cached_json_round_trips()
    {
        var doc = AiDocumentParser.Parse(AiJson.Full("Acme Cloud hosts apps.", "E1", "E2"), Ids)!;

        var again = AiDocumentParser.Parse(AiDocumentParser.ToJson(doc), Ids)!;

        Assert.Equal(doc.Overview, again.Overview);
        Assert.Equal(doc.Architecture, again.Architecture);
        Assert.Equal(doc.Endpoints, again.Endpoints);
        Assert.Equal(doc.Design, again.Design);
        Assert.Equal(doc.Tips, again.Tips);
    }

    // Cleaning is idempotent, so a cached row that is parsed again does not change.
    [Fact]
    public void Sanitiser_is_idempotent()
    {
        var once = AiProseSanitizer.Clean(Hostile, 400);

        Assert.Equal(once, AiProseSanitizer.Clean(once, 400));
    }

    // The sanitiser removes HTML, images and link targets and flattens newlines.
    [Fact]
    public void Sanitiser_removes_html_images_and_link_targets()
    {
        var clean = AiProseSanitizer.Clean(Hostile, 1000);

        Assert.DoesNotContain("<script", clean);
        Assert.DoesNotContain("<img", clean);
        Assert.DoesNotContain("javascript:", clean);
        Assert.DoesNotContain("evil/x.png", clean);
        Assert.DoesNotContain('\n', clean);
        Assert.Contains("click", clean);
    }

    // Hostile text, once escaped for the document, cannot produce raw HTML, a link, an image, a
    // heading, a list item or an extra table cell.
    [Fact]
    public void Hostile_text_is_inert_after_escaping()
    {
        var escaped = MarkdownText.Inline(AiProseSanitizer.Clean(Hostile, 1000), 1000);

        Assert.DoesNotContain("<", escaped.Replace("\\<", string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain(">", escaped.Replace("\\>", string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain("://", escaped);
        Assert.DoesNotContain("www.", escaped);
        Assert.False(escaped.StartsWith('#'));
        Assert.Equal(0, Unescaped(escaped, '|'));
        Assert.Equal(0, Unescaped(escaped, '`'));
        Assert.Equal(0, Unescaped(escaped, '['));
        Assert.Equal(0, Unescaped(escaped, '*'));
        Assert.DoesNotContain('\n', escaped);
    }

    // Counts occurrences of a character not preceded by a backslash.
    internal static int Unescaped(string text, char c)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == c)
            {
                count++;
            }
        }

        return count;
    }
}
