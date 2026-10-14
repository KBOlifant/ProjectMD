using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectMD.Api.Services.Extraction;

// Finds the GraphQL operations in a request body, keeping only each operation's kind and name (for
// example "query GetUser"). The query text and the variables are read only far enough to get those
// two words and are never kept. A POST to a path containing "graphql", or any JSON POST body with a
// "query" field, counts as GraphQL. A batched body (a JSON array) gives one operation per item.
// Unnamed operations become "anonymous query" (or "anonymous mutation"...). Pure.
public static partial class GraphQlOperations
{
    // At most this many operations are taken from one batched body.
    private const int MaxOperations = 10;

    // "query Name", "mutation Name(...)", "subscription", optionally preceded by whitespace.
    [GeneratedRegex(@"^\s*(query|mutation|subscription)\b\s*([_A-Za-z][_0-9A-Za-z]{0,59})?", RegexOptions.CultureInvariant, 250)]
    private static partial Regex OperationStart();

    // A valid GraphQL name.
    [GeneratedRegex(@"^[_A-Za-z][_0-9A-Za-z]{0,59}$", RegexOptions.CultureInvariant, 250)]
    private static partial Regex ValidName();

    // Returns the operation labels found in the request, or an empty list when it is not GraphQL.
    public static IReadOnlyList<string> Parse(string method, string path, ReadOnlyMemory<byte> body)
    {
        if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase) || body.IsEmpty || body.Length > JsonShape.MaxBodyBytes)
        {
            return [];
        }

        var span = body.Span;
        var first = 0;
        while (first < span.Length && span[first] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            first++;
        }

        if (first >= span.Length || span[first] is not ((byte)'{' or (byte)'['))
        {
            return [];
        }

        var pathLooksGraphQl = path.Contains("graphql", StringComparison.OrdinalIgnoreCase);
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
            var operations = new List<string>();
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    if (operations.Count >= MaxOperations)
                    {
                        break;
                    }

                    AddOperation(item, pathLooksGraphQl, operations);
                }
            }
            else
            {
                AddOperation(root, pathLooksGraphQl, operations);
            }

            return operations;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // Adds the label for one request object if it looks like a GraphQL operation.
    private static void AddOperation(JsonElement item, bool pathLooksGraphQl, List<string> operations)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        string? query = null;
        string? operationName = null;
        var hasQuery = false;
        foreach (var property in item.EnumerateObject())
        {
            if (property.NameEquals("query") && property.Value.ValueKind == JsonValueKind.String)
            {
                query = property.Value.GetString();

                // On a path that is not obviously GraphQL, a "query" field only counts when its
                // text reads like a GraphQL document (a search box also posts {"query": "shoes"}).
                hasQuery = pathLooksGraphQl || LooksLikeDocument(query);
            }
            else if (property.NameEquals("operationName") && property.Value.ValueKind == JsonValueKind.String)
            {
                operationName = property.Value.GetString();
            }
        }

        if (!hasQuery && !(pathLooksGraphQl && operationName is not null))
        {
            return;
        }

        var (kind, name) = ReadHeader(query);
        if (operationName is not null && SafeIsMatch(ValidName(), operationName))
        {
            name = operationName;
        }

        operations.Add(name is null ? "anonymous " + kind : kind + " " + name);
    }

    // True when the text starts like a GraphQL document: "{", query, mutation, subscription or fragment.
    private static bool LooksLikeDocument(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var head = StripComments(text.Length > 400 ? text[..400] : text).TrimStart();
        return head.StartsWith('{') ||
               head.StartsWith("query", StringComparison.Ordinal) ||
               head.StartsWith("mutation", StringComparison.Ordinal) ||
               head.StartsWith("subscription", StringComparison.Ordinal) ||
               head.StartsWith("fragment", StringComparison.Ordinal);
    }

    // The kind and name at the start of a query text. A text that starts with "{" is an anonymous
    // query; anything unrecognised is also treated as an anonymous query.
    private static (string Kind, string? Name) ReadHeader(string? query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return ("query", null);
        }

        var head = query.Length > 400 ? query[..400] : query;
        try
        {
            var match = OperationStart().Match(StripComments(head));
            if (match.Success)
            {
                var name = match.Groups[2].Success ? match.Groups[2].Value : null;
                return (match.Groups[1].Value, name);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Treat as anonymous.
        }

        return ("query", null);
    }

    // Drops "# comment" lines at the start of the text.
    private static string StripComments(string text)
    {
        var lines = text.Split('\n');
        var index = 0;
        while (index < lines.Length && (lines[index].TrimStart().StartsWith('#') || lines[index].Trim().Length == 0))
        {
            index++;
        }

        return string.Join('\n', lines[index..]);
    }

    // Regex match that treats a timeout as "no match".
    private static bool SafeIsMatch(Regex regex, string input)
    {
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
