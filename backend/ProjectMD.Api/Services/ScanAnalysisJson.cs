using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectMD.Api.Models;
using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Extraction.Components;

namespace ProjectMD.Api.Services;

// Turns a ScanAnalysis into JSON and back (camelCase, same shape the website receives), and keeps
// what we store small and free of anything that looks like a secret.
public static class ScanAnalysisJson
{
    // The biggest serialised analysis we store: 64 KB.
    public const int MaxBytes = 64 * 1024;

    // Current version of the analysis shape.
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    // CSS variable names that suggest a secret.
    private static readonly string[] SecretWords =
        ["token", "secret", "password", "passwd", "apikey", "api-key", "api_key", "auth", "credential", "private", "session", "cookie"];

    // Value prefixes that suggest a secret.
    private static readonly string[] SecretPrefixes =
        ["bearer ", "basic ", "sk-", "sk_", "gsk_", "pk_live", "sk_live", "ghp_", "xox", "eyj"];

    // Writes the analysis as JSON text.
    public static string Serialize(ScanAnalysis analysis) => JsonSerializer.Serialize(analysis, Options);

    // Reads JSON text; null or damaged text gives null (the scan simply has no analysis).
    public static ScanAnalysis? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ScanAnalysis>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Builds the analysis from a page digest and the AI-written component previews, or null when
    // none of design, API or components was captured.
    public static ScanAnalysis? FromDigest(PageDigest digest, ComponentPreviews? components = null)
    {
        if (digest.Design is null && digest.Api is null && components is null)
        {
            return null;
        }

        return Limit(new ScanAnalysis(CurrentVersion, digest.Design, digest.Api, components));
    }

    // Removes secret-looking CSS variables, then shrinks until the JSON fits in MaxBytes: first the
    // component previews, then the endpoint response shapes, then the endpoints, then the whole API
    // part, then the CSS variables.
    public static ScanAnalysis Limit(ScanAnalysis analysis)
    {
        var current = analysis with { Design = Scrub(analysis.Design) };
        if (Fits(current))
        {
            return current;
        }

        current = current with { Components = null };
        if (Fits(current))
        {
            return current;
        }

        if (current.Api is { } api)
        {
            var noShapes = api with
            {
                Endpoints = api.Endpoints.Select(e => e with { ResponseShape = null }).ToList(),
                Truncated = true,
            };
            current = current with { Api = noShapes };
            if (Fits(current))
            {
                return current;
            }

            current = current with { Api = noShapes with { Endpoints = [] } };
            if (Fits(current))
            {
                return current;
            }

            current = current with { Api = null };
            if (Fits(current))
            {
                return current;
            }
        }

        if (current.Design is { } design)
        {
            current = current with { Design = design with { CssVariables = [] } };
            if (Fits(current))
            {
                return current;
            }
        }

        // Cannot happen with the capture's own limits; keep only the version rather than refuse the scan.
        return new ScanAnalysis(current.Version, null, null);
    }

    private static bool Fits(ScanAnalysis a) => Encoding.UTF8.GetByteCount(Serialize(a)) <= MaxBytes;

    // Drops CSS variables whose name or value looks like a secret.
    private static DesignSystem? Scrub(DesignSystem? design) =>
        design is null
            ? null
            : design with { CssVariables = design.CssVariables.Where(v => !LooksSecret(v)).ToList() };

    private static bool LooksSecret(CssVariable v)
    {
        var name = v.Name.ToLowerInvariant();
        if (SecretWords.Any(name.Contains))
        {
            return true;
        }

        var value = v.Value.Trim().ToLowerInvariant();
        return SecretPrefixes.Any(value.StartsWith);
    }
}
