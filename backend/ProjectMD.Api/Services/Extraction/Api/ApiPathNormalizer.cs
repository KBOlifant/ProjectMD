using System.Text;
using System.Text.RegularExpressions;

namespace ProjectMD.Api.Services.Extraction;

// Turns a request path such as /api/users/42/orders/9f1c... into a template such as
// /api/users/{id}/orders/{id}, so calls that differ only by an identifier are grouped together. It
// also keeps anything that looks like a token or a secret out of what is stored: a long random
// segment is replaced before it is kept or logged. Readable words such as "users", "v1" and
// "search" are kept. Pure and deterministic.
public static partial class ApiPathNormalizer
{
    // What an identifier-like segment becomes.
    public const string IdPlaceholder = "{id}";

    // Paths deeper than this are cut (the rest is not stored).
    private const int MaxSegments = 12;

    // A template longer than this is cut.
    private const int MaxTemplateChars = 160;

    // Letters, digits, dash, underscore only.
    [GeneratedRegex(@"^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant, 250)]
    private static partial Regex SimpleWord();

    // A number with dashes, underscores or colons between digits (dates, ranges, compound ids).
    [GeneratedRegex(@"^\d[\d_:-]*$", RegexOptions.CultureInvariant, 250)]
    private static partial Regex NumberLike();

    // Builds the template for a path (the part of the URL before the "?"). The result always starts
    // with "/". Query strings and fragments must already be removed by the caller.
    public static string Template(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder();
        var count = Math.Min(segments.Length, MaxSegments);
        for (var i = 0; i < count; i++)
        {
            builder.Append('/').Append(LooksLikeIdentifier(segments[i]) ? IdPlaceholder : Clean(segments[i]));
        }

        if (segments.Length > MaxSegments)
        {
            builder.Append("/...");
        }

        if (builder.Length == 0)
        {
            return "/";
        }

        var template = builder.ToString();
        return template.Length > MaxTemplateChars ? template[..MaxTemplateChars] : template;
    }

    // Removes anything unusual from a kept segment so the template is plain text.
    private static string Clean(string segment)
    {
        var builder = new StringBuilder(segment.Length);
        foreach (var c in segment)
        {
            builder.Append(char.IsControl(c) || char.IsWhiteSpace(c) ? '_' : c);
        }

        return builder.ToString();
    }

    // True when a path segment is probably an identifier, not a word: a number, a UUID, a long hex
    // string, a token-looking string, or 8+ characters mixing letters and digits.
    public static bool LooksLikeIdentifier(string segment)
    {
        if (segment.Length == 0)
        {
            return false;
        }

        if (segment.IndexOfAny(['@', '%', '=', ',']) >= 0)
        {
            return true;
        }

        if (segment.All(char.IsAsciiDigit))
        {
            return true;
        }

        if (SafeIsMatch(NumberLike(), segment))
        {
            return true;
        }

        if (LooksLikeToken(segment))
        {
            return true;
        }

        if (segment.Length >= 8 && SafeIsMatch(SimpleWord(), segment))
        {
            var hasLetter = segment.Any(char.IsAsciiLetter);
            var hasDigit = segment.Any(char.IsAsciiDigit);
            if (hasLetter && hasDigit && !IsReadableCompound(segment))
            {
                return true;
            }
        }

        return segment.Length >= 40;
    }

    // True for the strong cases only (used for JSON field names too): a UUID, a long hex string, a
    // long mixed letter-and-digit string, or a very long run of base64-like characters.
    public static bool LooksLikeToken(string text)
    {
        if (text.Length < 16)
        {
            return text.Length == 8 && text.All(char.IsAsciiHexDigit) && text.Any(char.IsAsciiDigit) && text.Any(char.IsAsciiLetter);
        }

        if (Guid.TryParse(text, out _))
        {
            return true;
        }

        if (text.All(char.IsAsciiHexDigit))
        {
            return true;
        }

        if (!SafeIsMatch(SimpleWord(), text))
        {
            return false;
        }

        var hasLetter = text.Any(char.IsAsciiLetter);
        var hasDigit = text.Any(char.IsAsciiDigit);
        return (hasLetter && hasDigit && !IsReadableCompound(text)) || text.Length >= 32;
    }

    // "top-10-products" or "oauth2_token": words (and short numbers) joined by - or _ read as words,
    // not as an identifier.
    private static bool IsReadableCompound(string text)
    {
        var parts = text.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        return parts.All(part =>
            part.All(char.IsAsciiLetter) && part.Length <= 14 ||
            part.Length <= 3 && part.All(char.IsAsciiDigit) ||
            part.Length <= 8 && part.Any(char.IsAsciiLetter) && part.Count(char.IsAsciiDigit) <= 1);
    }

    // Regex match that treats a timeout as "no match".
    private static bool SafeIsMatch(Regex regex, string input)
    {
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
