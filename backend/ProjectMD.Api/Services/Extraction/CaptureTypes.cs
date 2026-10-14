namespace ProjectMD.Api.Services.Extraction;

// Data types for the two extra things a scan can capture: the API calls the page makes, and the
// design system it uses. They hold facts only (names, shapes, counts, colours), never secret
// values such as tokens, cookies, query-string values or response bodies.

// One API call the page made, grouped with identical calls. PathTemplate has changing parts
// replaced (for example /api/users/{id}). Statuses are the distinct HTTP codes seen. ResponseShape
// is a short description of the JSON layout such as "{id:number,name:string,items:[{sku:string}]}"
// (field names and types only, never values), or null when it was not JSON. GraphQlOperation is
// the operation name and kind, for example "query GetUser", for GraphQL calls. QueryParams are the
// parameter NAMES only.
public sealed record ApiEndpoint(
    string Method,
    string Host,
    string PathTemplate,
    bool FirstParty,
    int Count,
    IReadOnlyList<int> Statuses,
    string? ContentType,
    string? ResponseShape,
    string? GraphQlOperation,
    IReadOnlyList<string> QueryParams);

// A third-party host the page talked to (analytics, fonts, payments...), with how many requests and
// a plain category when it is a well-known service (null when unknown).
public sealed record ThirdPartyHost(string Host, int Requests, string? Category);

// Everything captured about the page's API traffic. Truncated is true when the endpoint list was cut
// to its size limit.
public sealed record ApiCatalog(
    IReadOnlyList<ApiEndpoint> Endpoints,
    IReadOnlyList<ThirdPartyHost> ThirdParty,
    bool Truncated);

// One colour used on the page: a lower-case #rrggbb value, how many sampled elements used it, and a
// role guess ("text", "background", "border" or "accent").
public sealed record ColorToken(string Hex, int Uses, string Role);

// One font family with the weights and font sizes (for example "16px") seen for it, most used first.
public sealed record FontToken(string Family, IReadOnlyList<int> Weights, IReadOnlyList<string> Sizes);

// A CSS custom property declared on the page root, for example --color-primary = #3b82f6.
public sealed record CssVariable(string Name, string Value);

// A compact summary of the visual design: the main colours, fonts, corner radii, shadows, spacing
// steps, root CSS variables and breakpoints. UtilityFramework is "Tailwind" (or similar) when most
// elements use utility classes, otherwise null.
public sealed record DesignSystem(
    IReadOnlyList<ColorToken> Colors,
    IReadOnlyList<FontToken> Fonts,
    IReadOnlyList<string> Radii,
    IReadOnlyList<string> Shadows,
    IReadOnlyList<string> SpacingScale,
    IReadOnlyList<CssVariable> CssVariables,
    IReadOnlyList<string> Breakpoints,
    string? UtilityFramework);
