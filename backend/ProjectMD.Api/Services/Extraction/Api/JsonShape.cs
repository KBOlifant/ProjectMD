using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectMD.Api.Services.Extraction;

// Describes the layout of a JSON response using type names only, for example
// {id:number,name:string,items:[{sku:string,price:number}]}. No value from the response is ever
// copied: strings, numbers and booleans become their type names. Containers nest at most three
// levels (deeper ones show as just "object" or "array"), an array is described by its first
// element, and an object shows at most 20 keys followed by "...". A key that looks like data (a
// UUID, a long token, text with spaces) is shown as {key}, so maps keyed by ids do not leak them.
// Bodies larger than 64 KB or not valid JSON give null.
public static partial class JsonShape
{
    // The largest response body that is examined.
    public const int MaxBodyBytes = 64 * 1024;

    private const int MaxDepth = 3;
    private const int MaxKeys = 20;

    // A field name that is safe to show as it is.
    [GeneratedRegex(@"^[A-Za-z_$@][A-Za-z0-9_.$@-]{0,39}$", RegexOptions.CultureInvariant, 250)]
    private static partial Regex PlainName();

    // Returns the shape of the JSON body, or null when it is empty, too big, or not valid JSON.
    public static string? Describe(ReadOnlyMemory<byte> body)
    {
        if (body.Length == 0 || body.Length > MaxBodyBytes)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 64 });
            var builder = new StringBuilder();
            Write(document.RootElement, 1, builder);
            return builder.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Writes the description of one value.
    private static void Write(JsonElement element, int depth, StringBuilder output)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(element, depth, output);
                break;
            case JsonValueKind.Array:
                WriteArray(element, depth, output);
                break;
            case JsonValueKind.String:
                output.Append("string");
                break;
            case JsonValueKind.Number:
                output.Append("number");
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                output.Append("boolean");
                break;
            default:
                output.Append("null");
                break;
        }
    }

    // {key:type,...}: first 20 distinct keys, in the order they appear.
    private static void WriteObject(JsonElement element, int depth, StringBuilder output)
    {
        if (depth > MaxDepth)
        {
            output.Append("object");
            return;
        }

        output.Append('{');
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var written = 0;
        var more = false;
        foreach (var property in element.EnumerateObject())
        {
            var name = SafeName(property.Name);
            if (!seen.Add(name))
            {
                continue;
            }

            if (written >= MaxKeys)
            {
                more = true;
                break;
            }

            if (written > 0)
            {
                output.Append(',');
            }

            output.Append(name).Append(':');
            Write(property.Value, depth + 1, output);
            written++;
        }

        if (more)
        {
            output.Append(",...");
        }

        output.Append('}');
    }

    // [type]: the first element stands for all; an empty array is [].
    private static void WriteArray(JsonElement element, int depth, StringBuilder output)
    {
        if (depth > MaxDepth)
        {
            output.Append("array");
            return;
        }

        output.Append('[');
        foreach (var item in element.EnumerateArray())
        {
            Write(item, depth + 1, output);
            break;
        }

        output.Append(']');
    }

    // A field name safe to store: plain identifiers are kept; anything that looks like data is
    // replaced by {key}.
    private static string SafeName(string name)
    {
        try
        {
            if (PlainName().IsMatch(name) && !ApiPathNormalizer.LooksLikeToken(name))
            {
                return name;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Fall through to the placeholder.
        }

        return "{key}";
    }
}
