using System.Net;
using System.Text.RegularExpressions;

namespace ProjectMD.Api.Services.Extraction;

// Recognises the "you are a bot" pages that bot-protection services (Cloudflare, Akamai, Imperva,
// DataDome and others) show instead of the real site. Such a page must not be documented as if it
// were the site, and the scan should say plainly why it stopped. Pure: it only looks at the status,
// the headers and the page title. It never tries to get past the protection.
public static partial class BlockDetector
{
    // Titles of well-known challenge and block pages (lower case, matched anywhere in the title).
    private static readonly string[] BlockTitles =
    [
        "attention required! | cloudflare",
        "just a moment...",
        "just a moment…",
        "access denied",
        "are you a robot",
        "are you human",
        "pardon our interruption",
        "request unsuccessful. incapsula",
        "bot verification",
        "security check",
        "ddos-guard",
        "please verify you are a human",
    ];

    [GeneratedRegex(@"<title[^>]*>(?<t>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex TitleRegex();

    // True when the page is a bot-protection challenge or block page.
    // Cloudflare marks its challenges with the cf-mitigated header; otherwise the title decides,
    // but only together with an error status or a very small page, so a real article that happens
    // to be titled "Access denied" is not refused.
    public static bool LooksBlocked(int status, IReadOnlyDictionary<string, string> headers, string? html)
    {
        if (headers.TryGetValue("cf-mitigated", out var mitigated) && !string.IsNullOrWhiteSpace(mitigated))
        {
            return true;
        }

        var title = Title(html);
        if (title is null || !BlockTitles.Any(t => title.Contains(t, StringComparison.Ordinal)))
        {
            return false;
        }

        var smallPage = (html?.Length ?? 0) < 60_000;
        return status is 403 or 429 or 503 || smallPage;
    }

    // The page title in lower case with entities decoded and spaces collapsed; null when missing.
    internal static string? Title(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return null;
        }

        try
        {
            var head = html.Length > 20_000 ? html[..20_000] : html;
            var match = TitleRegex().Match(head);
            if (!match.Success)
            {
                return null;
            }

            var text = WebUtility.HtmlDecode(match.Groups["t"].Value);
            var title = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
            return title.Length == 0 ? null : title;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }
}
