using Microsoft.Playwright;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Tests.Extraction;

// Tests for the pure helpers the fetcher uses (header allowlist, error mapping, text cutting).
public class FetchSupportTests
{
    // Only the allowlisted headers survive, keys are lower case, and cookies are reduced to names.
    [Fact]
    public void Headers_are_filtered_to_the_allowlist_and_cookie_values_are_never_kept()
    {
        var all = new Dictionary<string, string>
        {
            ["Server"] = "nginx/1.25",
            ["X-Powered-By"] = "Next.js",
            ["Set-Cookie"] = "session=SECRETVALUE; Path=/; HttpOnly\n_ga=GA1.1.123; Domain=.example.com\nsession=again",
            ["Authorization"] = "Bearer should-not-be-here",
            ["Content-Type"] = "text/html; charset=utf-8",
            ["X-Internal-Debug"] = "10.0.0.5",
            ["cf-ray"] = "abc-AMS",
        };

        var kept = FetchSupport.FilterHeaders(all);

        Assert.Equal("nginx/1.25", kept["server"]);
        Assert.Equal("Next.js", kept["x-powered-by"]);
        Assert.Equal("text/html; charset=utf-8", kept["content-type"]);
        Assert.Equal("abc-AMS", kept["cf-ray"]);
        Assert.Equal("session, _ga", kept["set-cookie"]);
        Assert.DoesNotContain("authorization", kept.Keys);
        Assert.DoesNotContain("x-internal-debug", kept.Keys);
        Assert.DoesNotContain(kept.Values, v => v.Contains("SECRETVALUE") || v.Contains("GA1.1"));
    }

    // Very long header values are cut.
    [Fact]
    public void Long_header_values_are_cut()
    {
        var kept = FetchSupport.FilterHeaders(new Dictionary<string, string> { ["via"] = new string('v', 5000) });

        Assert.Equal(200, kept["via"].Length);
    }

    // Cookie names only, in order, no duplicates, no attributes.
    [Fact]
    public void Cookie_names_are_extracted()
    {
        Assert.Equal("a, b", FetchSupport.CookieNames("a=1; Path=/\nb=2\na=3"));
        Assert.Equal("", FetchSupport.CookieNames("=novalue\n;;;"));
    }

    // HTML and XHTML are pages; JSON, images, PDF and plain text are not; a missing type is let through.
    [Theory]
    [InlineData("text/html", true)]
    [InlineData("text/html; charset=UTF-8", true)]
    [InlineData("TEXT/HTML", true)]
    [InlineData("application/xhtml+xml", true)]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("application/json", false)]
    [InlineData("application/pdf", false)]
    [InlineData("image/png", false)]
    [InlineData("text/plain", false)]
    [InlineData("application/octet-stream", false)]
    public void Content_type_check(string? contentType, bool isHtml)
    {
        Assert.Equal(isHtml, FetchSupport.IsHtmlContentType(contentType));
    }

    // Browser errors become ExtractionExceptions with safe messages and the right kind.
    [Theory]
    [InlineData("net::ERR_NAME_NOT_RESOLVED at https://10.1.2.3.internal/", ExtractionFailure.Unreachable)]
    [InlineData("net::ERR_CONNECTION_REFUSED at http://192.168.0.5:8080/", ExtractionFailure.Unreachable)]
    [InlineData("net::ERR_CERT_AUTHORITY_INVALID at https://example.com/", ExtractionFailure.Unreachable)]
    [InlineData("net::ERR_TOO_MANY_REDIRECTS at https://example.com/", ExtractionFailure.Unreachable)]
    [InlineData("net::ERR_BLOCKED_BY_CLIENT at http://169.254.169.254/", ExtractionFailure.BlockedAddress)]
    [InlineData("net::ERR_UNSAFE_PORT at http://example.com:22/", ExtractionFailure.BlockedAddress)]
    [InlineData("Download is starting", ExtractionFailure.NotHtml)]
    [InlineData("net::ERR_TIMED_OUT at https://example.com/", ExtractionFailure.TimedOut)]
    [InlineData("Executable doesn't exist at C:\\Users\\x\\ms-playwright\\chromium\\chrome.exe", ExtractionFailure.RendererMissing)]
    [InlineData("something else entirely went wrong at https://10.0.0.1/", ExtractionFailure.Unreachable)]
    public void Browser_errors_are_mapped_to_safe_messages(string message, ExtractionFailure expected)
    {
        var mapped = FetchSupport.Map(new PlaywrightException(message));

        Assert.Equal(expected, mapped.Failure);
        Assert.DoesNotContain("10.", mapped.Message);
        Assert.DoesNotContain("192.168", mapped.Message);
        Assert.DoesNotContain("169.254", mapped.Message);
        Assert.DoesNotContain("http", mapped.Message);
        Assert.DoesNotContain("\\", mapped.Message);
        Assert.DoesNotContain(" at ", mapped.Message);
    }

    // The missing-browser message is exactly the documented one.
    [Fact]
    public void Missing_browser_message_is_exact()
    {
        var mapped = FetchSupport.Map(new PlaywrightException("Executable doesn't exist at /x/chrome"));

        Assert.Equal("The page renderer is not installed on the server", mapped.Message);
        Assert.Equal(FetchSupport.RendererMissingMessage, mapped.Message);
    }

    // Timeouts become TimedOut; an ExtractionException passes through unchanged.
    [Fact]
    public void Timeouts_and_extraction_exceptions_are_handled()
    {
        Assert.Equal(ExtractionFailure.TimedOut, FetchSupport.Map(new TimeoutException("Timeout 30000ms exceeded.")).Failure);

        var original = new ExtractionException(ExtractionFailure.TooLarge, "too big");
        Assert.Same(original, FetchSupport.Map(original));
    }

    // Guard decisions map to user-safe exceptions.
    [Fact]
    public void Guard_decisions_map_to_exceptions()
    {
        Assert.Equal(ExtractionFailure.BlockedAddress, FetchSupport.FromGuard(new GuardDecision(GuardVerdict.BlockedAddress, "x")).Failure);
        Assert.Equal(ExtractionFailure.BlockedAddress, FetchSupport.FromGuard(new GuardDecision(GuardVerdict.BlockedScheme, "x")).Failure);
        Assert.Equal(ExtractionFailure.Unreachable, FetchSupport.FromGuard(new GuardDecision(GuardVerdict.DnsFailure, "x")).Failure);
        Assert.Equal(ExtractionFailure.Unreachable, FetchSupport.FromGuard(new GuardDecision(GuardVerdict.TooManyRedirects, "x")).Failure);
    }

    // IsClientError separates "your address is wrong" (400) from "the site or renderer failed" (502).
    [Theory]
    [InlineData(ExtractionFailure.BlockedAddress, true)]
    [InlineData(ExtractionFailure.NotHtml, true)]
    [InlineData(ExtractionFailure.Unreachable, false)]
    [InlineData(ExtractionFailure.TimedOut, false)]
    [InlineData(ExtractionFailure.TooLarge, false)]
    [InlineData(ExtractionFailure.RendererMissing, false)]
    [InlineData(ExtractionFailure.HttpError, false)]
    [InlineData(ExtractionFailure.Busy, false)]
    public void Client_error_flag(ExtractionFailure failure, bool isClientError)
    {
        Assert.Equal(isClientError, new ExtractionException(failure, "m").IsClientError);
    }

    // The string-only constructor from the contract works.
    [Fact]
    public void Message_only_constructor()
    {
        var ex = new ExtractionException("The page renderer is not installed on the server");

        Assert.Equal("The page renderer is not installed on the server", ex.Message);
        Assert.Equal(ExtractionFailure.Unreachable, ex.Failure);
    }
}

// Tests for the small text helpers.
public class TextToolsTests
{
    // Whitespace of every kind collapses to single spaces.
    [Fact]
    public void Collapse_normalises_whitespace()
    {
        Assert.Equal("a b c", TextTools.Collapse("  a \n\t b\u00A0\u200B c  "));
        Assert.Equal("", TextTools.Collapse(null));
    }

    // Cuts happen at word boundaries and the ellipsis counts toward the limit.
    [Theory]
    [InlineData("alpha beta gamma delta", 14, "alpha beta…")]
    [InlineData("alpha beta gamma delta", 11, "alpha beta…")]
    [InlineData("alpha beta gamma delta", 100, "alpha beta gamma delta")]
    [InlineData("alpha beta", 6, "alpha…")]
    public void Truncate_at_word(string text, int max, string expected)
    {
        var result = TextTools.TruncateAtWord(text, max);

        Assert.Equal(expected, result);
        Assert.True(result.Length <= Math.Max(max, text.Length));
    }

    // A first word longer than the limit gives nothing rather than half a word.
    [Fact]
    public void Truncate_at_word_never_cuts_inside_the_only_word()
    {
        Assert.Equal("", TextTools.TruncateAtWord("supercalifragilistic", 8));
    }

    // UTF-8 cutting never splits a character.
    [Fact]
    public void Truncate_utf8_does_not_split_characters()
    {
        var text = "ab\u00E9\u20AC\U0001F600cd"; // 1 + 1 + 2 + 3 + 4 + 1 + 1 bytes
        Assert.Equal("ab", TextTools.TruncateUtf8(text, 3));
        Assert.Equal("ab\u00E9", TextTools.TruncateUtf8(text, 4));
        Assert.Equal("ab\u00E9\u20AC", TextTools.TruncateUtf8(text, 8));
        Assert.Equal("ab\u00E9\u20AC\U0001F600", TextTools.TruncateUtf8(text, 11));
        Assert.Equal(text, TextTools.TruncateUtf8(text, 100));
        Assert.Equal("", TextTools.TruncateUtf8(text, 0));
    }

    // chars / 4 rounded up.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(12000, 3000)]
    public void Estimate_tokens(int chars, int tokens)
    {
        Assert.Equal(tokens, TextTools.EstimateTokens(chars));
    }
}
