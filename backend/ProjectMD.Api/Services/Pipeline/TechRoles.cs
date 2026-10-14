using System.Text.RegularExpressions;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Services.Pipeline;

// Works out the "Version" and "Role" columns of the tech table. A scan only stores technology
// names, so the role comes from the fingerprint rules' category for that name (looked up once) and
// the version only appears when the name itself ends in one (for example "WordPress 6.4").
public static partial class TechRoles
{
    // A trailing version such as " 6.4" or " v18.2.0".
    [GeneratedRegex(@"^(?<name>.*\S)\s+v?(?<version>\d+(?:\.\d+)*)$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    // Category name -> reader-friendly role, built from the embedded fingerprint rules.
    private static readonly Lazy<IReadOnlyDictionary<string, string>> RoleByName = new(() =>
    {
        var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in TechRuleSet.LoadEmbedded().Rules)
        {
            roles[rule.Name] = RoleLabel(rule.Category);
        }

        return roles;
    });

    // Splits "WordPress 6.4" into ("WordPress", "6.4"); without a version the version is null.
    public static (string Name, string? Version) Split(string name)
    {
        var match = VersionRegex().Match(name.Trim());
        return match.Success
            ? (match.Groups["name"].Value, match.Groups["version"].Value)
            : (name.Trim(), null);
    }

    // The role of a technology, or null when it is not a known one.
    public static string? RoleFor(string name) =>
        RoleByName.Value.TryGetValue(name, out var role) ? role : null;

    // Category -> label shown in the table.
    private static string RoleLabel(string category) => category switch
    {
        "platform" => "Platform / CMS",
        "framework" => "Framework",
        "host" => "Hosting",
        "cdn" => "CDN",
        "css" => "CSS framework",
        "library" => "Library",
        "service" => "Third-party service",
        "analytics" => "Analytics",
        "font" => "Fonts",
        _ => "Other",
    };
}
