namespace ProjectMD.Api.Services.Extraction;

// One answered request, as the proxy saw it. Url is the address the content really came from;
// RequestBody and ResponseBody are only looked at (to find a GraphQL operation name and the JSON
// layout) and are never kept.
public sealed record ApiObservation(
    string Method,
    Uri Url,
    string ResourceType,
    int Status,
    string? ContentType,
    ReadOnlyMemory<byte> RequestBody,
    ReadOnlyMemory<byte> ResponseBody);

// Collects the API calls a page makes while it loads and turns them into an ApiCatalog.
//
// What is kept: for first-party xhr/fetch calls, the method, host, path template, status, media type,
// the JSON layout (types only), the GraphQL operation (kind and name only) and query parameter
// NAMES. For other hosts only a request count per host plus a category for well-known services.
// What is never kept: bodies, header values, cookies, query-string values, fragments, user info,
// GraphQL variables or any value from a response. Nothing is logged. The recorder never throws:
// if anything goes wrong it marks itself failed and Build returns null, so a scan is never lost
// to capture trouble. It is safe to call from several threads at once.
public sealed class ApiRecorder
{
    // At most this many first-party endpoints are returned.
    public const int MaxEndpoints = 40;

    // Memory bounds while recording (the page can make many odd calls).
    private const int MaxGroups = 300;
    private const int MaxThirdPartyHosts = 200;
    private const int MaxReturnedThirdParty = 30;
    private const int MaxQueryParams = 10;
    private const int MaxStatuses = 8;
    private const int MaxShapeAttempts = 3;
    private const int MaxShapeChars = 600;

    private readonly object _gate = new();
    private readonly Dictionary<string, Group> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _thirdParty = new(StringComparer.OrdinalIgnoreCase);
    private string? _pageHost;
    private bool _overflow;
    private bool _failed;

    // What is known about one endpoint so far.
    private sealed class Group
    {
        public required string Method { get; init; }

        public required string Host { get; init; }

        public required string Template { get; init; }

        public string? Operation { get; init; }

        public int Count { get; set; }

        public SortedSet<int> Statuses { get; } = [];

        public string? ContentType { get; set; }

        public string? Shape { get; set; }

        public int ShapeAttempts { get; set; }

        public SortedSet<string> QueryParams { get; } = new(StringComparer.Ordinal);
    }

    // Creates a recorder. The page address can be given now or later with SetPage.
    public ApiRecorder(Uri? pageUrl = null)
    {
        _pageHost = pageUrl?.IdnHost.ToLowerInvariant();
    }

    // Tells the recorder which site is being scanned (calls to the same registrable domain are
    // first party). Called again after a redirect, the latest address wins.
    public void SetPage(Uri pageUrl)
    {
        lock (_gate)
        {
            _pageHost = pageUrl.IdnHost.ToLowerInvariant();
        }
    }

    // Records one answered request. Never throws.
    public void Record(ApiObservation observation)
    {
        try
        {
            lock (_gate)
            {
                RecordCore(observation);
            }
        }
        catch (Exception)
        {
            // Capture is a bonus. Remember that it broke, say nothing about why (the details could
            // contain captured data), and carry on.
            lock (_gate)
            {
                _failed = true;
            }
        }
    }

    // Builds the catalog from what was recorded, or null if capture failed or nothing was seen.
    public ApiCatalog? Build()
    {
        try
        {
            lock (_gate)
            {
                if (_failed || (_groups.Count == 0 && _thirdParty.Count == 0))
                {
                    return null;
                }

                var ordered = _groups.Values
                    .OrderByDescending(g => g.Count)
                    .ThenBy(g => g.Host, StringComparer.Ordinal)
                    .ThenBy(g => g.Template, StringComparer.Ordinal)
                    .ThenBy(g => g.Method, StringComparer.Ordinal)
                    .ThenBy(g => g.Operation ?? string.Empty, StringComparer.Ordinal)
                    .ToList();

                var endpoints = ordered
                    .Take(MaxEndpoints)
                    .Select(g => new ApiEndpoint(
                        g.Method, g.Host, g.Template, true, g.Count, [.. g.Statuses], g.ContentType, g.Shape, g.Operation, [.. g.QueryParams]))
                    .ToList();

                var thirdParty = _thirdParty
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Take(MaxReturnedThirdParty)
                    .Select(pair => new ThirdPartyHost(pair.Key, pair.Value, HostClassifier.Category(pair.Key)))
                    .ToList();

                return new ApiCatalog(endpoints, thirdParty, _overflow || ordered.Count > MaxEndpoints);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    // The recording work; called with the lock held.
    private void RecordCore(ApiObservation o)
    {
        var host = o.Url.IdnHost.ToLowerInvariant();
        var firstParty = _pageHost is not null && HostClassifier.IsFirstParty(host, _pageHost);

        if (!firstParty)
        {
            if (_thirdParty.ContainsKey(host) || _thirdParty.Count < MaxThirdPartyHosts)
            {
                _thirdParty[host] = _thirdParty.GetValueOrDefault(host) + 1;
            }

            return;
        }

        var isDataCall = o.ResourceType.Equals("xhr", StringComparison.OrdinalIgnoreCase) ||
                         o.ResourceType.Equals("fetch", StringComparison.OrdinalIgnoreCase);
        if (!isDataCall)
        {
            return;
        }

        var method = o.Method.ToUpperInvariant();
        var template = ApiPathNormalizer.Template(o.Url.AbsolutePath);
        var operations = GraphQlOperations.Parse(method, o.Url.AbsolutePath, o.RequestBody);
        var labels = operations.Count == 0 ? new List<string?> { null } : operations.Select(op => (string?)op).ToList();
        var mediaType = MediaType(o.ContentType);
        var paramNames = QueryParamNames(o.Url.Query);

        foreach (var operation in labels)
        {
            var key = method + "|" + host + "|" + template + "|" + operation;
            if (!_groups.TryGetValue(key, out var group))
            {
                if (_groups.Count >= MaxGroups)
                {
                    _overflow = true;
                    continue;
                }

                group = new Group { Method = method, Host = host, Template = template, Operation = operation };
                _groups[key] = group;
            }

            group.Count++;
            if (group.Statuses.Count < MaxStatuses)
            {
                group.Statuses.Add(o.Status);
            }

            group.ContentType ??= mediaType;
            foreach (var name in paramNames)
            {
                if (group.QueryParams.Count < MaxQueryParams)
                {
                    group.QueryParams.Add(name);
                }
            }

            if (group.Shape is null && group.ShapeAttempts < MaxShapeAttempts && labels.Count == 1 && IsJson(mediaType))
            {
                group.ShapeAttempts++;
                var shape = JsonShape.Describe(o.ResponseBody);
                group.Shape = shape is { Length: > MaxShapeChars } ? shape[..MaxShapeChars] : shape;
            }
        }
    }

    // True for application/json, application/problem+json, application/graphql-response+json...
    private static bool IsJson(string? mediaType) =>
        mediaType is not null && mediaType.Contains("json", StringComparison.Ordinal);

    // The media type without parameters (charset...), lower case, or null.
    private static string? MediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var media = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return media.Length is > 0 and <= 80 ? media : null;
    }

    // The parameter NAMES in a query string (values are skipped, never read further than the "=").
    // Odd names (very long, or with unusual characters) are dropped.
    private static IEnumerable<string> QueryParamNames(string query)
    {
        if (query.Length <= 1)
        {
            yield break;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var raw = equals < 0 ? pair : pair[..equals];
            string name;
            try
            {
                name = Uri.UnescapeDataString(raw.Replace('+', ' '));
            }
            catch (UriFormatException)
            {
                continue;
            }

            if (IsPlainName(name) && !ApiPathNormalizer.LooksLikeToken(name))
            {
                yield return name;
            }
        }
    }

    // A parameter name made of letters, digits and a few punctuation marks, up to 40 characters.
    private static bool IsPlainName(string name) =>
        name.Length is > 0 and <= 40 &&
        (char.IsAsciiLetter(name[0]) || name[0] is '_' or '$') &&
        name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '[' or ']' or '$');
}
