using System.Collections.Concurrent;
using System.Net;

namespace ProjectMD.Api.Services.Extraction;

// Looks up the IP addresses behind a host name. Kept behind an interface so the guard can be tested
// with a fake and never needs the network.
public interface IDnsResolver
{
    // Returns every A and AAAA address for the host. May throw when the name does not resolve.
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct);
}

// The real resolver: asks the operating system's DNS.
public sealed class SystemDnsResolver : IDnsResolver
{
    // Resolves the host with Dns.GetHostAddressesAsync (all address families).
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct) =>
        await Dns.GetHostAddressesAsync(host, ct);
}

// The answer of the guard for one request.
public enum GuardVerdict
{
    // The request may go ahead.
    Allowed,

    // The text is not an absolute URL.
    InvalidUrl,

    // Only http and https are allowed (no file:, ftp:, data:, chrome: and so on).
    BlockedScheme,

    // The host is, or resolves to, an address that is not on the public internet.
    BlockedAddress,

    // The port is one that browsers refuse because it belongs to a non-web service (mail, databases...).
    BlockedPort,

    // The host name did not resolve (or resolving failed). Unresolvable means refused.
    DnsFailure,

    // The request is further along a redirect chain than allowed.
    TooManyRedirects,
}

// A verdict plus a short diagnostic for server logs (host name only, never shown to users). For an
// allowed request, Addresses holds the public IP addresses that were checked: the connection MUST
// be made to one of these, not to a fresh DNS answer (that is what pins the connection).
public sealed record GuardDecision(GuardVerdict Verdict, string Detail, IReadOnlyList<IPAddress>? Addresses = null)
{
    // True when the request may proceed.
    public bool Allowed => Verdict == GuardVerdict.Allowed;

    // An "allowed" answer carrying the checked addresses.
    public static GuardDecision Allow(IReadOnlyList<IPAddress> addresses) => new(GuardVerdict.Allowed, string.Empty, addresses);
}

// The SSRF gatekeeper. Every request the page makes (the first page, each redirect hop and every
// sub-resource) is checked here before anything is sent, and the connection is then made to the
// exact addresses checked here (see GuardedHttpClient), never to a second DNS answer.
// A request is allowed only when: it is http or https; the redirect chain is short enough; the port
// is not one browsers refuse; and the host is a public IP literal, or a name whose DNS records (all
// A and AAAA) are ALL public. If resolving fails or returns nothing the request is refused. "Public"
// is decided by UrlValidator.IsPublicAddress, the same rules used for the URL the user types.
// Each guard instance remembers the answer per host for the lifetime of one page fetch, which also
// makes the checked addresses the pinned addresses for that fetch (no DNS rebinding window).
public sealed class RequestGuard
{
    // How long one DNS lookup may take before it counts as a failure.
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(5);

    // Ports that browsers refuse to talk to (the Chromium restricted-port list) plus a few database
    // and admin ports, so a web scanner cannot be used to poke other kinds of services.
    private static readonly HashSet<int> BlockedPorts =
    [
        1, 7, 9, 11, 13, 15, 17, 19, 20, 21, 22, 23, 25, 37, 42, 43, 53, 69, 77, 79, 87, 95, 101, 102, 103, 104,
        109, 110, 111, 113, 115, 117, 119, 123, 135, 137, 139, 143, 161, 179, 389, 427, 465, 512, 513, 514, 515,
        526, 530, 531, 532, 540, 548, 554, 556, 563, 587, 601, 636, 989, 990, 993, 995, 1719, 1720, 1723, 2049,
        3659, 4045, 5060, 5061, 6000, 6566, 6665, 6666, 6667, 6668, 6669, 6679, 6697, 10080,
        2375, 2376, 3306, 5432, 5984, 6379, 9200, 9300, 11211, 27017,
    ];

    private readonly IDnsResolver _dns;
    private readonly int _maxRedirects;
    private readonly Func<IPAddress, bool> _isPublic;
    private readonly ConcurrentDictionary<string, GuardDecision> _hostDecisions = new(StringComparer.OrdinalIgnoreCase);

    // Creates a guard that uses the given resolver and allows at most maxRedirects hops per chain.
    public RequestGuard(IDnsResolver dns, int maxRedirects = 8)
        : this(dns, maxRedirects, UrlValidator.IsPublicAddress)
    {
    }

    // For tests only: lets a test treat loopback as "public" so a local server can stand in for a
    // website. The app always uses the public constructor.
    internal RequestGuard(IDnsResolver dns, int maxRedirects, Func<IPAddress, bool> isPublic)
    {
        _dns = dns;
        _maxRedirects = maxRedirects;
        _isPublic = isPublic;
    }

    // Decides whether a request may be made. redirectDepth is how many redirects led to this
    // request (0 for a request that was not a redirect).
    public async Task<GuardDecision> CheckAsync(string url, int redirectDepth, CancellationToken ct)
    {
        if (redirectDepth > _maxRedirects)
        {
            return new GuardDecision(GuardVerdict.TooManyRedirects, "redirect limit");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return new GuardDecision(GuardVerdict.InvalidUrl, "not an absolute url");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return new GuardDecision(GuardVerdict.BlockedScheme, $"scheme {uri.Scheme}");
        }

        if (BlockedPorts.Contains(uri.Port))
        {
            return new GuardDecision(GuardVerdict.BlockedPort, $"port {uri.Port}");
        }

        var host = uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
            ? uri.Host.Trim('[', ']')
            : uri.IdnHost.TrimEnd('.');

        return await CheckHostAsync(host, ct);
    }

    // Decides whether connections to this host are allowed, and returns the addresses to use. The
    // host is a name or an IP literal (no brackets). The first answer is remembered for this guard.
    public async Task<GuardDecision> CheckHostAsync(string host, CancellationToken ct)
    {
        if (host.Length == 0)
        {
            return new GuardDecision(GuardVerdict.InvalidUrl, "empty host");
        }

        // An IP written in the URL is judged directly; there is nothing to resolve.
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            return _isPublic(literal)
                ? GuardDecision.Allow([literal])
                : new GuardDecision(GuardVerdict.BlockedAddress, "ip literal is not public");
        }

        if (_hostDecisions.TryGetValue(host, out var known))
        {
            return known;
        }

        var decision = await ResolveAndJudgeAsync(host, ct);
        _hostDecisions[host] = decision;
        return decision;
    }

    // Resolves the name and allows it only when there is at least one address and all are public.
    private async Task<GuardDecision> ResolveAndJudgeAsync(string host, CancellationToken ct)
    {
        IReadOnlyList<IPAddress> addresses;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(DnsTimeout);
            addresses = await _dns.ResolveAsync(host, timeout.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Any failure (not found, timeout, resolver error) means we cannot prove it is public.
            return new GuardDecision(GuardVerdict.DnsFailure, $"could not resolve {host}");
        }

        if (addresses.Count == 0)
        {
            return new GuardDecision(GuardVerdict.DnsFailure, $"no addresses for {host}");
        }

        // One private address among public ones is enough to refuse: the browser might pick it.
        return addresses.All(_isPublic)
            ? GuardDecision.Allow(addresses.ToList())
            : new GuardDecision(GuardVerdict.BlockedAddress, $"{host} resolves to a non-public address");
    }
}
