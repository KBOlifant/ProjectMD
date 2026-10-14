using System.Text;
using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Services.Pipeline;

// Writes the "API endpoints" part of the scan document from the captured ApiCatalog: the endpoint
// table, the response shapes and the third-party services. Nothing here comes from the AI except
// the "Purpose" sentence, which is looked up by endpoint id.
internal static class MarkdownApiSection
{
    // How many endpoints get a response-shape block, and the longest shape text kept.
    private const int MaxShapeBlocks = 8;
    private const int MaxShapeChars = 600;

    // The most third-party hosts listed, and the longest host name.
    private const int MaxThirdPartyHosts = 30;

    // Appends the section, or nothing when no endpoint and no third-party service was captured.
    public static void Append(StringBuilder sb, PageDigest digest, AiDocument? ai)
    {
        var api = digest.Api;
        if (api is null || (api.Endpoints.Count == 0 && api.ThirdParty.Count == 0))
        {
            return;
        }

        sb.AppendLine("## API endpoints").AppendLine();
        var ordered = EndpointRanking.Order(api);
        if (ordered.Count > 0)
        {
            AppendTable(sb, ordered, digest.FinalUrl.Host, ai);
            if (api.Truncated)
            {
                sb.AppendLine("_The list is truncated: more endpoints were seen than are shown._").AppendLine();
            }

            AppendShapes(sb, ordered, digest.FinalUrl.Host);
        }
        else
        {
            sb.AppendLine("_No first-party API calls were captured._").AppendLine();
        }

        AppendThirdParty(sb, api.ThirdParty);
    }

    // Method | Path | Status | Type | Purpose, one row per endpoint in the fixed ranking order.
    private static void AppendTable(StringBuilder sb, IReadOnlyList<ApiEndpoint> ordered, string pageHost, AiDocument? ai)
    {
        var purposes = (ai?.Endpoints ?? []).ToDictionary(n => n.Id, n => n.Purpose, StringComparer.Ordinal);
        sb.AppendLine("| Method | Path | Status | Type | Purpose |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        for (var i = 0; i < ordered.Count; i++)
        {
            var e = ordered[i];
            var id = EndpointRanking.IdFor(i);
            var purpose = id is not null && purposes.TryGetValue(id, out var text)
                ? MarkdownText.Inline(text, AiDocumentLimits.EndpointPurposeChars + 10)
                : MarkdownDocumentBuilder.Blank;
            var statuses = e.Statuses.Distinct().Order().ToList();

            sb.Append("| ").Append(MarkdownText.Inline(e.Method, 10))
                .Append(" | ").Append(PathCell(e, pageHost))
                .Append(" | ").Append(statuses.Count == 0 ? MarkdownDocumentBuilder.Blank : string.Join(", ", statuses.Select(MarkdownText.Number)))
                .Append(" | ").Append(string.IsNullOrWhiteSpace(e.ContentType) ? MarkdownDocumentBuilder.Blank : MarkdownText.Inline(e.ContentType, 60))
                .Append(" | ").Append(purpose)
                .AppendLine(" |");
        }

        sb.AppendLine();
    }

    // The path in a code span (with the host when it is not the page's own), then the GraphQL
    // operation and the query parameter names when known.
    private static string PathCell(ApiEndpoint e, string pageHost)
    {
        var path = string.Equals(e.Host, pageHost, StringComparison.OrdinalIgnoreCase) ? e.PathTemplate : e.Host + e.PathTemplate;
        var cell = new StringBuilder(MarkdownText.Code(path, 160, inTable: true));
        if (!string.IsNullOrWhiteSpace(e.GraphQlOperation))
        {
            cell.Append(" (").Append(MarkdownText.Inline(e.GraphQlOperation, 80)).Append(')');
        }

        if (e.QueryParams.Count > 0)
        {
            cell.Append(" params: ").Append(string.Join(", ", e.QueryParams.Take(10).Select(p => MarkdownText.Inline(p, 40))));
        }

        return cell.ToString();
    }

    // A code block per endpoint (first 8 that have a shape): the field names and types only.
    private static void AppendShapes(StringBuilder sb, IReadOnlyList<ApiEndpoint> ordered, string pageHost)
    {
        var withShape = ordered.Where(e => !string.IsNullOrWhiteSpace(e.ResponseShape)).Take(MaxShapeBlocks).ToList();
        if (withShape.Count == 0)
        {
            return;
        }

        sb.AppendLine("### Response shapes").AppendLine();
        foreach (var e in withShape)
        {
            var path = string.Equals(e.Host, pageHost, StringComparison.OrdinalIgnoreCase) ? e.PathTemplate : e.Host + e.PathTemplate;
            sb.Append("**").Append(MarkdownText.Inline(e.Method, 10)).Append("** ").AppendLine(MarkdownText.Code(path, 160)).AppendLine();
            var shape = e.ResponseShape!.Length > MaxShapeChars ? e.ResponseShape[..MaxShapeChars] + "..." : e.ResponseShape;
            sb.AppendLine(MarkdownText.Fence("json", shape)).AppendLine();
        }
    }

    // Third-party hosts grouped by category (unknown ones under "other", last), with request counts.
    private static void AppendThirdParty(StringBuilder sb, IReadOnlyList<ThirdPartyHost> hosts)
    {
        if (hosts.Count == 0)
        {
            return;
        }

        sb.AppendLine("### Third-party services").AppendLine();
        var groups = hosts
            .Take(MaxThirdPartyHosts)
            .GroupBy(h => string.IsNullOrWhiteSpace(h.Category) ? "other" : h.Category.Trim().ToLowerInvariant())
            .OrderBy(g => g.Key == "other" ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var items = group
                .OrderByDescending(h => h.Requests)
                .ThenBy(h => h.Host, StringComparer.Ordinal)
                .Select(h => $"{MarkdownText.Code(h.Host, 100)} ({MarkdownText.Number(h.Requests)})");
            sb.Append("- **").Append(MarkdownText.Inline(group.Key, 40)).Append(":** ").AppendLine(string.Join(", ", items));
        }

        sb.AppendLine();
    }
}
