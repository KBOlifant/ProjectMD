using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ProjectMD.Api.Services.Extraction;

// Which part of the page a fingerprint signal looks at.
public enum TechSignalKind
{
    // The addresses of scripts the page loaded.
    Script,

    // The addresses of stylesheets the page loaded.
    Stylesheet,

    // Script or stylesheet addresses (either).
    Asset,

    // A response header: the value, or just its presence.
    Header,

    // The NAMES of cookies the server set.
    Cookie,

    // The <meta name="generator"> text.
    Generator,

    // A marker token read from the markup (exact match), such as "id:__next".
    Marker,

    // The addresses of links on the page.
    Link,

    // The visible text of the page.
    Text,
}

// One test inside a rule. Strong signals are specific enough to prove a technology on their own;
// weak ones only add to the score.
public sealed class TechSignal
{
    // Creates a signal. Exactly one of pattern and value is used (value only for markers).
    public TechSignal(TechSignalKind kind, Regex? pattern, string? header, string? value, bool strong)
    {
        Kind = kind;
        Pattern = pattern;
        Header = header;
        Value = value;
        Strong = strong;
    }

    // What the signal looks at.
    public TechSignalKind Kind { get; }

    // The regular expression to match (case-insensitive), when the kind uses one.
    public Regex? Pattern { get; }

    // The header name for Header signals (lower case).
    public string? Header { get; }

    // The exact marker for Marker signals.
    public string? Value { get; }

    // True when this signal alone is enough to report the technology.
    public bool Strong { get; }
}

// One technology and the signals that reveal it.
public sealed class TechRule
{
    // Creates a rule.
    public TechRule(string name, string category, IReadOnlyList<string> implies, IReadOnlyList<TechSignal> signals)
    {
        Name = name;
        Category = category;
        Implies = implies;
        Signals = signals;
    }

    // The display name, such as "Next.js".
    public string Name { get; }

    // One of: platform, framework, host, cdn, css, library, service, analytics, font.
    public string Category { get; }

    // Names of technologies that always come with this one.
    public IReadOnlyList<string> Implies { get; }

    // The tests.
    public IReadOnlyList<TechSignal> Signals { get; }
}

// The loaded and validated fingerprint rules (data from TechRules.json).
public sealed class TechRuleSet
{
    // The categories a rule may use.
    public static readonly IReadOnlyList<string> Categories =
        ["platform", "framework", "host", "cdn", "css", "library", "service", "analytics", "font"];

    // The name of the rules file inside the assembly (see the csproj).
    private const string ResourceName = "ProjectMD.Api.Services.Extraction.TechRules.json";

    // Longest time one pattern may run on page data.
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(250);

    // Creates a set from validated rules.
    private TechRuleSet(IReadOnlyList<TechRule> rules)
    {
        Rules = rules;
    }

    // All rules, in file order.
    public IReadOnlyList<TechRule> Rules { get; }

    // Loads the rules that ship inside the assembly.
    public static TechRuleSet LoadEmbedded()
    {
        using var stream = typeof(TechRuleSet).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The embedded tech rules file is missing.");
        return Load(stream);
    }

    // Reads and validates a rules file. Throws InvalidDataException when the file is not valid
    // (unknown signal type, bad regular expression, a rule without a strong signal, and so on).
    public static TechRuleSet Load(Stream json)
    {
        FileModel? model;
        try
        {
            model = JsonSerializer.Deserialize<FileModel>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The tech rules file is not valid JSON.", ex);
        }

        if (model?.Technologies is null)
        {
            throw new InvalidDataException("The tech rules file has no 'technologies' list.");
        }

        var rules = new List<TechRule>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in model.Technologies)
        {
            if (string.IsNullOrWhiteSpace(item.Name) || !names.Add(item.Name))
            {
                throw new InvalidDataException($"A technology name is missing or repeated: '{item.Name}'.");
            }

            if (item.Category is null || !Categories.Contains(item.Category))
            {
                throw new InvalidDataException($"'{item.Name}' has an unknown category '{item.Category}'.");
            }

            var name = item.Name;
            var signals = (item.Signals ?? []).Select(s => BuildSignal(name, s)).ToList();
            if (!signals.Any(s => s.Strong))
            {
                throw new InvalidDataException($"'{name}' needs at least one strong signal.");
            }

            rules.Add(new TechRule(name, item.Category, item.Implies ?? [], signals));
        }

        foreach (var rule in rules)
        {
            foreach (var implied in rule.Implies)
            {
                if (!names.Contains(implied))
                {
                    throw new InvalidDataException($"'{rule.Name}' implies '{implied}', which is not defined.");
                }
            }
        }

        return new TechRuleSet(rules);
    }

    // Validates one signal from the file and compiles its pattern.
    private static TechSignal BuildSignal(string tech, SignalModel s)
    {
        if (!Enum.TryParse<TechSignalKind>(s.Type, ignoreCase: true, out var kind))
        {
            throw new InvalidDataException($"'{tech}' has a signal of unknown type '{s.Type}'.");
        }

        if (kind == TechSignalKind.Marker)
        {
            return string.IsNullOrWhiteSpace(s.Value)
                ? throw new InvalidDataException($"'{tech}' has a marker signal without a value.")
                : new TechSignal(kind, null, null, s.Value, s.Strong);
        }

        if (kind == TechSignalKind.Header)
        {
            if (string.IsNullOrWhiteSpace(s.Header))
            {
                throw new InvalidDataException($"'{tech}' has a header signal without a header name.");
            }

            return new TechSignal(kind, s.Pattern is null ? null : Compile(tech, s.Pattern), s.Header.Trim().ToLowerInvariant(), null, s.Strong);
        }

        return string.IsNullOrWhiteSpace(s.Pattern)
            ? throw new InvalidDataException($"'{tech}' has a {s.Type} signal without a pattern.")
            : new TechSignal(kind, Compile(tech, s.Pattern), null, null, s.Strong);
    }

    // Compiles a pattern case-insensitively with a time limit.
    private static Regex Compile(string tech, string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, PatternTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"'{tech}' has an invalid pattern '{pattern}'.", ex);
        }
    }

    // Shapes of the JSON file (private: only used for reading).
    private sealed class FileModel
    {
        [JsonPropertyName("technologies")]
        public List<TechModel>? Technologies { get; set; }
    }

    private sealed class TechModel
    {
        public string? Name { get; set; }

        public string? Category { get; set; }

        public List<string>? Implies { get; set; }

        public List<SignalModel>? Signals { get; set; }
    }

    private sealed class SignalModel
    {
        public string? Type { get; set; }

        public string? Pattern { get; set; }

        public string? Header { get; set; }

        public string? Value { get; set; }

        public bool Strong { get; set; }
    }
}
