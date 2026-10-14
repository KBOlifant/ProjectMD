using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Services.Pipeline;

// The one place that decides the ORDER of captured API endpoints and which of them get an id
// (E1, E2, ...) in the prompt. The prompt and the renderer both use it, so an id always means the
// same call. The order does not depend on request counts or statuses, because those change between
// visits while the cache key (the content hash) deliberately ignores them: a cached answer for "E2"
// must still point at the same endpoint on the next scan.
public static class EndpointRanking
{
    // The prefix of every endpoint id.
    public const string IdPrefix = "E";

    // All endpoints in a fixed order: first-party first, then by path, method, host and GraphQL
    // operation (ordinal, so the order is the same on every machine).
    public static IReadOnlyList<ApiEndpoint> Order(ApiCatalog? api) =>
        api is null
            ? []
            : api.Endpoints
                .OrderByDescending(e => e.FirstParty)
                .ThenBy(e => e.PathTemplate, StringComparer.Ordinal)
                .ThenBy(e => e.Method, StringComparer.Ordinal)
                .ThenBy(e => e.Host, StringComparer.Ordinal)
                .ThenBy(e => e.GraphQlOperation ?? string.Empty, StringComparer.Ordinal)
                .ToList();

    // The id of the endpoint at zero-based `index` in the ordered list ("E1" for the first), or
    // null when it is past the first AiDocumentLimits.MaxPromptEndpoints.
    public static string? IdFor(int index) =>
        index >= 0 && index < AiDocumentLimits.MaxPromptEndpoints ? IdPrefix + (index + 1) : null;

    // The ids handed out for this catalog (E1..En), used to validate the model's answer.
    public static IReadOnlySet<string> KnownIds(ApiCatalog? api)
    {
        var count = Math.Min(AiDocumentLimits.MaxPromptEndpoints, api?.Endpoints.Count ?? 0);
        return Enumerable.Range(0, count).Select(i => IdPrefix + (i + 1)).ToHashSet(StringComparer.Ordinal);
    }
}
