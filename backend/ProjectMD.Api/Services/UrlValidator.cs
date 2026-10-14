using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace ProjectMD.Api.Services;

// The outcome of checking a URL typed by the user: either a clean absolute URL with its display
// host, or a message that is safe to show to the user.
public sealed record UrlValidationResult(bool IsValid, Uri? Uri, string? Url, string? Host, string? Error)
{
    // A valid result: the normalised Uri, its text form and the host without "www.".
    public static UrlValidationResult Success(Uri uri, string host) => new(true, uri, uri.AbsoluteUri, host, null);

    // An invalid result carrying the message to show.
    public static UrlValidationResult Failure(string error) => new(false, null, null, null, error);
}

// Checks and normalises the URL a user wants to scan.
public interface IUrlValidator
{
    // Returns the cleaned URL or a user-facing error. Never throws for bad input.
    UrlValidationResult Validate(string? input);
}

// The security boundary for scan URLs. It is a port of frontend/lib/url.ts parseUrl, made
// stricter: only http(s), at most 2048 characters, no credentials in the URL, a dotted public
// host name, and no local, private, link-local, CGNAT or otherwise non-public IP addresses
// (IPv4 and IPv6, including IPv4-mapped forms). The messages are the ones the frontend shows.
// This only looks at the text of the URL. It does not resolve DNS and fetches nothing.
// TODO(ssrf): a name that looks public can still resolve to a private address (for example
// 127.0.0.1.nip.io) or be rebound after this check. Whatever later fetches the page MUST resolve
// the host itself, re-check every resolved address with the same rules, connect to that address
// directly, and re-check again on every redirect. Do not trust this validation alone.
public sealed partial class UrlValidator : IUrlValidator
{
    // Longest URL we accept and store (matches the Scans.Url column).
    public const int MaxUrlLength = 2048;

    // Longest DNS name allowed (the Scans.Host column holds 255).
    private const int MaxHostLength = 253;

    public const string EmptyMessage = "Enter a URL to document.";
    public const string InvalidMessage = "That is not a valid URL. Try https://your-site.com/docs.";
    public const string SchemeMessage = "Only http and https URLs are supported.";
    public const string PrivateMessage = "Enter a public URL. Local and private addresses are not supported.";
    public const string TooLongMessage = "That URL is too long. Use one under 2048 characters.";
    public const string CredentialsMessage = "Remove the username and password from the URL.";

    // Name endings that only exist on private or local networks (host == suffix or ends with ".suffix").
    private static readonly string[] LocalSuffixes =
        ["localhost", "local", "internal", "localdomain", "lan", "intranet", "home.arpa"];

    // IPv4 ranges that are not on the public internet: network address and prefix length.
    private static readonly (byte[] Network, int Prefix)[] NonPublicIpv4 =
    [
        ([0, 0, 0, 0], 8),         // "this" network / unspecified
        ([10, 0, 0, 0], 8),        // private
        ([100, 64, 0, 0], 10),     // carrier-grade NAT
        ([127, 0, 0, 0], 8),       // loopback
        ([169, 254, 0, 0], 16),    // link-local (includes the cloud metadata address)
        ([172, 16, 0, 0], 12),     // private
        ([192, 0, 0, 0], 24),      // IETF protocol assignments
        ([192, 0, 2, 0], 24),      // documentation
        ([192, 88, 99, 0], 24),    // deprecated 6to4 relay
        ([192, 168, 0, 0], 16),    // private
        ([198, 18, 0, 0], 15),     // benchmarking
        ([198, 51, 100, 0], 24),   // documentation
        ([203, 0, 113, 0], 24),    // documentation
        ([224, 0, 0, 0], 4),       // multicast
        ([240, 0, 0, 0], 4),       // reserved and broadcast
    ];

    // Matches "scheme://" at the start (the same test the frontend uses to decide whether to add https://).
    [GeneratedRegex(@"^[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SchemeRegex();

    // One DNS label: letters, digits, "-" and "_", not starting or ending with "-", 1 to 63 long.
    [GeneratedRegex(@"^[a-z0-9_]([a-z0-9_-]{0,61}[a-z0-9_])?$", RegexOptions.CultureInvariant)]
    private static partial Regex LabelRegex();

    // Validates the user's text and returns the normalised URL and display host, or an error message.
    public UrlValidationResult Validate(string? input)
    {
        var raw = input?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            return UrlValidationResult.Failure(EmptyMessage);
        }

        // Cheap size and character checks first, before any parsing.
        if (raw.Length > MaxUrlLength)
        {
            return UrlValidationResult.Failure(TooLongMessage);
        }

        if (raw.Any(char.IsControl))
        {
            return UrlValidationResult.Failure(InvalidMessage);
        }

        var withScheme = SchemeRegex().IsMatch(raw) ? raw : "https://" + raw;
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var parsed))
        {
            return UrlValidationResult.Failure(InvalidMessage);
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return UrlValidationResult.Failure(SchemeMessage);
        }

        if (parsed.UserInfo.Length > 0)
        {
            return UrlValidationResult.Failure(CredentialsMessage);
        }

        var normalized = parsed;
        string host;

        switch (parsed.HostNameType)
        {
            case UriHostNameType.IPv4:
            case UriHostNameType.IPv6:
            {
                // .NET has already turned odd spellings (2130706433, 0x7f.1, 127.1, [::ffff:7f00:1])
                // into one canonical address, so we classify that address.
                if (!IPAddress.TryParse(parsed.Host.Trim('[', ']'), out var address))
                {
                    return UrlValidationResult.Failure(InvalidMessage);
                }

                if (!IsPublicAddress(address))
                {
                    return UrlValidationResult.Failure(PrivateMessage);
                }

                host = parsed.Host.ToLowerInvariant();
                break;
            }

            case UriHostNameType.Dns:
            {
                host = DisplayHost(parsed);
                if (!host.Contains('.') || IsLocalName(host))
                {
                    return UrlValidationResult.Failure(PrivateMessage);
                }

                if (host.Length > MaxHostLength || !host.Split('.').All(label => LabelRegex().IsMatch(label)))
                {
                    return UrlValidationResult.Failure(InvalidMessage);
                }

                // A name ending in a number (256.1.1.1, 1.2.3.4.5, foo.0x7f) is a broken or disguised
                // IP address, never a real domain. Real IPv4 spellings were already handled above.
                var topLabel = host[(host.LastIndexOf('.') + 1)..];
                if (topLabel.All(char.IsAsciiDigit) || topLabel.StartsWith("0x", StringComparison.Ordinal))
                {
                    return UrlValidationResult.Failure(InvalidMessage);
                }

                // Store non-ASCII names in their punycode form so the saved URL is plain ASCII.
                if (!string.Equals(parsed.Host, parsed.IdnHost, StringComparison.Ordinal))
                {
                    normalized = new UriBuilder(parsed) { Host = parsed.IdnHost }.Uri;
                }

                break;
            }

            default:
                return UrlValidationResult.Failure(InvalidMessage);
        }

        if (normalized.AbsoluteUri.Length > MaxUrlLength)
        {
            return UrlValidationResult.Failure(TooLongMessage);
        }

        return UrlValidationResult.Success(normalized, host);
    }

    // The host shown in the UI: lower case, punycode for international names, no trailing dot and
    // no leading "www." (the same as the frontend's hostname.replace(/^www\./, "")).
    public static string DisplayHost(Uri uri)
    {
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            return uri.Host.ToLowerInvariant();
        }

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    // True for "localhost", "*.local", "*.internal" and the other private-network name endings.
    private static bool IsLocalName(string host) =>
        LocalSuffixes.Any(suffix =>
            host.Equals(suffix, StringComparison.Ordinal) ||
            host.EndsWith("." + suffix, StringComparison.Ordinal));

    // True when the address can be reached on the public internet. Everything loopback, private,
    // link-local, CGNAT, multicast, reserved or unspecified is refused.
    public static bool IsPublicAddress(IPAddress address) =>
        address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicIpv4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPublicIpv6(address),
            _ => false,
        };

    // IPv4 check against the table of non-public ranges.
    private static bool IsPublicIpv4(byte[] bytes) =>
        !NonPublicIpv4.Any(range => InRange(bytes, range.Network, range.Prefix));

    // IPv6 check. Forms that wrap an IPv4 address (IPv4-mapped, NAT64, 6to4) are judged by the
    // IPv4 address inside. Otherwise only global unicast (2000::/3) is allowed, minus the
    // documentation and protocol-assignment blocks. That excludes ::, ::1, fc00::/7, fe80::/10,
    // fec0::/10, ff00::/8 and the IPv4-compatible ::a.b.c.d form.
    private static bool IsPublicIpv6(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsPublicIpv4(address.MapToIPv4().GetAddressBytes());
        }

        var b = address.GetAddressBytes();

        // NAT64 well-known prefix 64:ff9b::/96 carries an IPv4 address in the last 4 bytes.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b[4..12].All(x => x == 0))
        {
            return IsPublicIpv4(b[12..16]);
        }

        // 6to4 2002::/16 carries an IPv4 address in bytes 2 to 5.
        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return IsPublicIpv4(b[2..6]);
        }

        // Not global unicast.
        if ((b[0] & 0xE0) != 0x20)
        {
            return false;
        }

        // 2001::/23 (protocol assignments, Teredo), 2001:db8::/32 and 3fff::/20 (documentation).
        if (b[0] == 0x20 && b[1] == 0x01 && (b[2] & 0xFE) == 0x00)
        {
            return false;
        }

        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8)
        {
            return false;
        }

        if (b[0] == 0x3f && b[1] == 0xff && (b[2] & 0xF0) == 0x00)
        {
            return false;
        }

        return true;
    }

    // True when the first `prefix` bits of the address equal the first `prefix` bits of the network.
    private static bool InRange(byte[] address, byte[] network, int prefix)
    {
        var fullBytes = prefix / 8;
        for (var i = 0; i < fullBytes; i++)
        {
            if (address[i] != network[i])
            {
                return false;
            }
        }

        var remainingBits = prefix % 8;
        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (address[fullBytes] & mask) == (network[fullBytes] & mask);
    }
}
