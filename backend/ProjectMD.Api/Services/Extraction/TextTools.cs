using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ProjectMD.Api.Services.Extraction;

// Small text helpers shared by the fetcher and the reducer. All are pure.
public static partial class TextTools
{
    // Longest time a regular expression may run on page text (page text is untrusted input).
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    // Anything volatile that changes between two loads of the same page: UUIDs, ISO timestamps,
    // clock times, epoch numbers, long hex strings, long mixed letter+digit tokens, and
    // "csrf=...", "nonce: ..." style pairs.
    private static readonly Regex[] VolatilePatterns =
    [
        new(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.CultureInvariant, RegexTimeout),
        new(@"\b\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})?", RegexOptions.CultureInvariant, RegexTimeout),
        new(@"\b\d{1,2}:\d{2}(?::\d{2})?(?:\s?[AaPp][Mm])?\b", RegexOptions.CultureInvariant, RegexTimeout),
        new(@"\b1\d{9,12}\b", RegexOptions.CultureInvariant, RegexTimeout),
        new(@"\b[0-9a-fA-F]{16,}\b", RegexOptions.CultureInvariant, RegexTimeout),
        new(@"\b(?=[A-Za-z0-9_\-+/=]*\d)(?=[A-Za-z0-9_\-+/=]*[A-Za-z])[A-Za-z0-9_\-+/=]{24,}", RegexOptions.CultureInvariant, RegexTimeout),
        new(@"\b(?:csrf|xsrf|nonce|session(?:[_-]?id)?|sessid|sid|token|authenticity[_-]?token|request[_-]?id|trace[_-]?id)\b\s*[:=]\s*\S+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout),
    ];

    // Runs of any whitespace (including no-break space) and invisible characters.
    [GeneratedRegex(@"[\s ​‌‍⁠﻿­]+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    // Turns any run of whitespace into one space and trims the ends. Null becomes "".
    public static string Collapse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    // Cuts the text to at most maxChars characters at a word boundary (never in the middle of a
    // word) and adds "…" when something was cut. The ellipsis counts toward maxChars. If the first
    // word alone is longer than the limit, an empty string is returned.
    public static string TruncateAtWord(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        if (maxChars <= 1)
        {
            return string.Empty;
        }

        var limit = maxChars - 1;
        var cut = text.LastIndexOf(' ', Math.Min(limit, text.Length - 1));

        // A space exactly after the limit also ends a whole word.
        if (cut < 0)
        {
            return string.Empty;
        }

        var head = text[..cut].TrimEnd();
        return head.Length == 0 ? string.Empty : head + "…";
    }

    // Cuts the text so its UTF-8 form is at most maxBytes long, without splitting a character or a
    // surrogate pair.
    public static string TruncateUtf8(string text, int maxBytes)
    {
        if (text.Length == 0 || maxBytes <= 0)
        {
            return string.Empty;
        }

        // Every char is at most 3 bytes (a surrogate pair is 4 bytes for 2 chars), so a short text
        // can be returned without counting.
        if ((long)text.Length * 3 <= maxBytes)
        {
            return text;
        }

        var bytes = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            int size;
            int chars;
            if (c < 0x80)
            {
                size = 1;
                chars = 1;
            }
            else if (c < 0x800)
            {
                size = 2;
                chars = 1;
            }
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                size = 4;
                chars = 2;
            }
            else
            {
                size = 3;
                chars = 1;
            }

            if (bytes + size > maxBytes)
            {
                break;
            }

            bytes += size;
            i += chars;
        }

        return i >= text.Length ? text : text[..i];
    }

    // The token estimate used everywhere: ceil(chars / 4). It is an approximation (English text is
    // about 4 characters per token), good enough for budgeting, not exact for any one model.
    public static int EstimateTokens(int chars) => (int)(((long)Math.Max(chars, 0) + 3) / 4);

    // Removes the parts of a text that change from one load to the next (see VolatilePatterns) and
    // collapses the whitespace left behind. The result is only used to build the content hash.
    public static string ScrubVolatile(string text)
    {
        var scrubbed = text;
        foreach (var pattern in VolatilePatterns)
        {
            try
            {
                scrubbed = pattern.Replace(scrubbed, string.Empty);
            }
            catch (RegexMatchTimeoutException)
            {
                // Pathological input: skip this pattern rather than fail the page.
            }
        }

        return Collapse(scrubbed);
    }

    // The lower-case hex SHA-256 of the UTF-8 text (64 characters).
    public static string Sha256Hex(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
