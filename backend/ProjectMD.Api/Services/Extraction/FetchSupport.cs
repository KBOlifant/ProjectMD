using Microsoft.Playwright;

namespace ProjectMD.Api.Services.Extraction;

// Pure helpers for the page fetcher, kept apart from the browser code so they can be tested.
public static class FetchSupport
{
    // The user-facing messages. None contains a URL, an address or a stack trace.
    public const string RendererMissingMessage = "The page renderer is not installed on the server";
    public const string RendererFailedMessage = "The page renderer could not be started on the server";
    public const string UnreachableMessage = "The site could not be reached. Check the address and try again.";
    public const string NotFoundMessage = "That website could not be found. Check the address and try again.";
    public const string CertificateMessage = "The site's secure (HTTPS) connection could not be verified.";
    public const string TooManyRedirectsMessage = "The site redirected too many times.";
    public const string TimedOutMessage = "The site took too long to respond.";
    public const string BlockedMessage = "That address is not allowed. Enter a public website URL.";
    public const string NotHtmlMessage = "That address is not a web page (it did not return HTML).";
    public const string TooLargeMessage = "That page is too large to scan.";
    public const string BusyMessage = "The scanner is busy right now. Try again in a minute.";

    // Response headers that help identify technologies. Everything else is dropped.
    // set-cookie is special: only the cookie NAMES are kept, never values.
    private static readonly HashSet<string> HeaderAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "server", "x-powered-by", "via", "x-vercel-id", "cf-ray", "x-generator", "content-type", "set-cookie",
        "x-amz-cf-id", "x-cache", "x-served-by", "x-fastly-request-id", "x-nf-request-id",
        "x-github-request-id", "x-wix-request-id", "x-shopify-stage",
    };

    // The most characters kept from one header value.
    private const int MaxHeaderValueChars = 200;

    // Keeps only the allowlisted headers, with keys in lower case. set-cookie becomes a comma
    // separated list of cookie names. Values are cut to a safe length.
    public static IReadOnlyDictionary<string, string> FilterHeaders(IEnumerable<KeyValuePair<string, string>> all)
    {
        var kept = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawKey, value) in all)
        {
            var key = rawKey.Trim().ToLowerInvariant();
            if (!HeaderAllowlist.Contains(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var cleaned = key == "set-cookie" ? CookieNames(value) : TextTools.Collapse(value);
            if (cleaned.Length > 0)
            {
                kept[key] = cleaned.Length > MaxHeaderValueChars ? cleaned[..MaxHeaderValueChars] : cleaned;
            }
        }

        return kept;
    }

    // From the raw set-cookie text (one cookie per line, as the browser reports it) returns the
    // cookie names only, comma separated. Values and attributes (Path, Domain...) are discarded.
    public static string CookieNames(string setCookie)
    {
        var names = new List<string>();
        foreach (var line in setCookie.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var name = line[..equals].Trim();
            if (name.Length is > 0 and <= 80 && !names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }

        return string.Join(", ", names);
    }

    // True when the Content-Type says the body is an HTML document (or the type is unknown).
    public static bool IsHtmlContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return true;
        }

        var type = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return type is "text/html" or "application/xhtml+xml";
    }

    // Turns whatever went wrong into an ExtractionException with a safe message. The original is
    // kept as the inner exception for server logs.
    public static ExtractionException Map(Exception ex)
    {
        if (ex is ExtractionException already)
        {
            return already;
        }

        // Playwright reports its own timeouts as System.TimeoutException.
        if (ex is System.TimeoutException)
        {
            return new ExtractionException(ExtractionFailure.TimedOut, TimedOutMessage, ex);
        }

        var text = ex.Message;
        if (IsMissingBrowser(ex))
        {
            return new ExtractionException(ExtractionFailure.RendererMissing, RendererMissingMessage, ex);
        }

        if (text.Contains("Download is starting", StringComparison.OrdinalIgnoreCase))
        {
            return new ExtractionException(ExtractionFailure.NotHtml, NotHtmlMessage, ex);
        }

        if (text.Contains("ERR_BLOCKED_BY_CLIENT", StringComparison.Ordinal) ||
            text.Contains("ERR_UNSAFE_PORT", StringComparison.Ordinal))
        {
            return new ExtractionException(ExtractionFailure.BlockedAddress, BlockedMessage, ex);
        }

        if (text.Contains("ERR_NAME_NOT_RESOLVED", StringComparison.Ordinal))
        {
            return new ExtractionException(ExtractionFailure.Unreachable, NotFoundMessage, ex);
        }

        if (text.Contains("ERR_CERT_", StringComparison.Ordinal) || text.Contains("ERR_SSL_", StringComparison.Ordinal))
        {
            return new ExtractionException(ExtractionFailure.Unreachable, CertificateMessage, ex);
        }

        if (text.Contains("ERR_TOO_MANY_REDIRECTS", StringComparison.Ordinal))
        {
            return new ExtractionException(ExtractionFailure.Unreachable, TooManyRedirectsMessage, ex);
        }

        if (text.Contains("ERR_TIMED_OUT", StringComparison.Ordinal) || text.Contains("ERR_CONNECTION_TIMED_OUT", StringComparison.Ordinal))
        {
            return new ExtractionException(ExtractionFailure.TimedOut, TimedOutMessage, ex);
        }

        return new ExtractionException(ExtractionFailure.Unreachable, UnreachableMessage, ex);
    }

    // True when the exception says the browser executable is not installed.
    public static bool IsMissingBrowser(Exception ex)
    {
        var text = ex.Message;
        return text.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("playwright install", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("download new browsers", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("is not found at", StringComparison.OrdinalIgnoreCase);
    }

    // The safe exception for a request the guard refused during the main page navigation.
    public static ExtractionException FromGuard(GuardDecision decision) => decision.Verdict switch
    {
        GuardVerdict.DnsFailure => new ExtractionException(ExtractionFailure.Unreachable, NotFoundMessage),
        GuardVerdict.TooManyRedirects => new ExtractionException(ExtractionFailure.Unreachable, TooManyRedirectsMessage),
        _ => new ExtractionException(ExtractionFailure.BlockedAddress, BlockedMessage),
    };
}
