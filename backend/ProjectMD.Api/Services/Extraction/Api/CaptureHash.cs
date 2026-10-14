using System.Text;

namespace ProjectMD.Api.Services.Extraction;

// Builds the text that stands for "what API calls and which design system this page has" inside the
// content hash, so the cache is invalidated when the API surface or the design changes. It is
// order-independent (everything is sorted) and leaves out anything that fluctuates between loads:
// request counts, HTTP statuses, usage counts and response shapes. When nothing was captured the
// result is empty, so pages without capture keep the same hash as before this feature existed.
public static class CaptureHash
{
    // The canonical form of the captured data, or "" when there is none.
    public static string Canonical(ApiCatalog? api, DesignSystem? design)
    {
        var lines = new List<string>();
        AddApi(api, lines);
        AddDesign(design, lines);
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        lines.Sort(StringComparer.Ordinal);
        var builder = new StringBuilder();
        foreach (var line in lines.Distinct(StringComparer.Ordinal))
        {
            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    // One line per endpoint: method, template, first-party flag, GraphQL operation.
    private static void AddApi(ApiCatalog? api, List<string> lines)
    {
        if (api is null)
        {
            return;
        }

        foreach (var endpoint in api.Endpoints)
        {
            lines.Add($"api|{endpoint.Method}|{endpoint.PathTemplate}|{(endpoint.FirstParty ? 1 : 0)}|{endpoint.GraphQlOperation}");
        }
    }

    // One line per design fact, without usage counts.
    private static void AddDesign(DesignSystem? design, List<string> lines)
    {
        if (design is null)
        {
            return;
        }

        foreach (var color in design.Colors)
        {
            lines.Add($"design.color|{color.Hex}|{color.Role}");
        }

        foreach (var font in design.Fonts)
        {
            var weights = string.Join(',', font.Weights.OrderBy(w => w));
            var sizes = string.Join(',', font.Sizes.OrderBy(s => s, StringComparer.Ordinal));
            lines.Add($"design.font|{font.Family}|{weights}|{sizes}");
        }

        lines.AddRange(design.Radii.Select(r => "design.radius|" + r));
        lines.AddRange(design.Shadows.Select(s => "design.shadow|" + s));
        lines.AddRange(design.SpacingScale.Select(s => "design.space|" + s));
        lines.AddRange(design.CssVariables.Select(v => $"design.var|{v.Name}|{v.Value}"));
        lines.AddRange(design.Breakpoints.Select(b => "design.bp|" + b));
        if (design.UtilityFramework is not null)
        {
            lines.Add("design.framework|" + design.UtilityFramework);
        }
    }
}
