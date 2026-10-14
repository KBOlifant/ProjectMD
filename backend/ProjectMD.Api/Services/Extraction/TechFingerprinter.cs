using System.Text.RegularExpressions;
using ProjectMD.Api.Models;

namespace ProjectMD.Api.Services.Extraction;

// Finds the technologies a page uses by checking the digest against the fixed rules in
// TechRules.json. No network, no AI, same input gives the same output.
// Scoring: a strong signal is worth 3 points and a weak one 1. A technology is reported only when
// at least one strong signal matched. Technologies implied by a reported one (Next.js implies React)
// are added with 1 point. Results are ordered platform, framework, host, CDN, CSS, library, service,
// analytics, font; the best framework/platform and the best host/CDN go first and are the only ones
// marked Highlighted. At most 8 are returned.
public sealed class TechFingerprinter : ITechFingerprinter
{
    // The most technologies returned.
    public const int MaxResults = 8;

    // Points for a matching strong and weak signal.
    private const int StrongPoints = 3;

    private const int WeakPoints = 1;

    private readonly TechRuleSet _rules;

    // Uses the given rules (the app registers the embedded set).
    public TechFingerprinter(TechRuleSet rules)
    {
        _rules = rules;
    }

    // One reported technology while the list is being built.
    private sealed record Hit(TechRule Rule, int Score);

    // Detects the technologies for a digest.
    public IReadOnlyList<ScanTech> Detect(PageDigest digest)
    {
        ArgumentNullException.ThrowIfNull(digest);

        var evidence = new Evidence(digest);
        var hits = new List<Hit>();

        foreach (var rule in _rules.Rules)
        {
            var score = 0;
            var strong = false;
            foreach (var signal in rule.Signals)
            {
                if (Matches(signal, evidence))
                {
                    score += signal.Strong ? StrongPoints : WeakPoints;
                    strong |= signal.Strong;
                }
            }

            if (strong)
            {
                hits.Add(new Hit(rule, score));
            }
        }

        // Things that always come with a detected technology.
        foreach (var hit in hits.ToList())
        {
            foreach (var impliedName in hit.Rule.Implies)
            {
                if (hits.All(h => !h.Rule.Name.Equals(impliedName, StringComparison.OrdinalIgnoreCase)))
                {
                    var implied = _rules.Rules.First(r => r.Name.Equals(impliedName, StringComparison.OrdinalIgnoreCase));
                    hits.Add(new Hit(implied, WeakPoints));
                }
            }
        }

        var sorted = hits
            .OrderBy(h => CategoryRank(h.Rule.Category))
            .ThenByDescending(h => h.Score)
            .ThenBy(h => h.Rule.Name, StringComparer.Ordinal)
            .ToList();

        // Lead with the best framework/platform and the best host/CDN; they are the highlighted pair.
        var leadFramework = sorted.FirstOrDefault(h => CategoryRank(h.Rule.Category) <= 1);
        var leadHost = sorted.FirstOrDefault(h => CategoryRank(h.Rule.Category) is 2 or 3);
        var ordered = new List<Hit>();
        if (leadFramework is not null)
        {
            ordered.Add(leadFramework);
        }

        if (leadHost is not null)
        {
            ordered.Add(leadHost);
        }

        ordered.AddRange(sorted.Where(h => h != leadFramework && h != leadHost));

        return ordered
            .Take(MaxResults)
            .Select((h, index) => new ScanTech
            {
                Name = h.Rule.Name,
                Highlighted = index < 2 && CategoryRank(h.Rule.Category) <= 3,
            })
            .ToList();
    }

    // The sort order of the categories (lower comes first).
    private static int CategoryRank(string category) => category switch
    {
        "platform" => 0,
        "framework" => 1,
        "host" => 2,
        "cdn" => 3,
        "css" => 4,
        "library" => 5,
        "service" => 6,
        "analytics" => 7,
        "font" => 8,
        _ => 9,
    };

    // The pieces of the digest that signals look at, prepared once per page.
    private sealed class Evidence
    {
        // Prepares the data from a digest.
        public Evidence(PageDigest digest)
        {
            Scripts = digest.ScriptUrls;
            Stylesheets = digest.StylesheetUrls;
            Assets = digest.ScriptUrls.Concat(digest.StylesheetUrls).ToList();
            Headers = digest.Headers;
            Links = digest.Links.Select(l => l.Href).ToList();
            Text = digest.Text;

            var markers = digest.Markers ?? [];
            Markers = new HashSet<string>(markers, StringComparer.OrdinalIgnoreCase);
            Generators = markers
                .Where(m => m.StartsWith("generator:", StringComparison.OrdinalIgnoreCase))
                .Select(m => m["generator:".Length..])
                .ToList();

            // The fetcher stores cookie NAMES only, comma separated, under "set-cookie".
            Cookies = HeaderValue("set-cookie")?
                .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList() ?? [];
        }

        public IReadOnlyList<string> Scripts { get; }

        public IReadOnlyList<string> Stylesheets { get; }

        public IReadOnlyList<string> Assets { get; }

        public IReadOnlyDictionary<string, string> Headers { get; }

        public IReadOnlyList<string> Links { get; }

        public string Text { get; }

        public HashSet<string> Markers { get; }

        public IReadOnlyList<string> Generators { get; }

        public IReadOnlyList<string> Cookies { get; }

        // A header value by name, ignoring case (the dictionary may be case-sensitive).
        public string? HeaderValue(string name)
        {
            foreach (var (key, value) in Headers)
            {
                if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }

            return null;
        }
    }

    // True when the signal matches the page evidence.
    private static bool Matches(TechSignal signal, Evidence e)
    {
        switch (signal.Kind)
        {
            case TechSignalKind.Script:
                return AnyMatch(signal.Pattern, e.Scripts);
            case TechSignalKind.Stylesheet:
                return AnyMatch(signal.Pattern, e.Stylesheets);
            case TechSignalKind.Asset:
                return AnyMatch(signal.Pattern, e.Assets);
            case TechSignalKind.Link:
                return AnyMatch(signal.Pattern, e.Links);
            case TechSignalKind.Cookie:
                return AnyMatch(signal.Pattern, e.Cookies);
            case TechSignalKind.Generator:
                return AnyMatch(signal.Pattern, e.Generators);
            case TechSignalKind.Marker:
                return signal.Value is not null && e.Markers.Contains(signal.Value);
            case TechSignalKind.Text:
                return AnyMatch(signal.Pattern, [e.Text]);
            case TechSignalKind.Header:
            {
                var value = signal.Header is null ? null : e.HeaderValue(signal.Header);
                return value is not null && (signal.Pattern is null || AnyMatch(signal.Pattern, [value]));
            }

            default:
                return false;
        }
    }

    // True when the pattern matches at least one of the values (a timeout counts as no match).
    private static bool AnyMatch(Regex? pattern, IEnumerable<string> values)
    {
        if (pattern is null)
        {
            return false;
        }

        foreach (var value in values)
        {
            try
            {
                if (pattern.IsMatch(value))
                {
                    return true;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Treat a pathological value as no match.
            }
        }

        return false;
    }
}
