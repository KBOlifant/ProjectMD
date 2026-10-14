using System.Globalization;
using System.Text;

namespace ProjectMD.Api.Services.Pipeline;

// Helpers that make untrusted text (page titles, captured paths, AI sentences) safe to put into the
// scan document. Every value that reaches the Markdown goes through one of these, so page content
// can never add headings, links, images, HTML, extra table columns or close a code block.
public static class MarkdownText
{
    // Characters that could start formatting, a link, an image, HTML or a table cell.
    private const string SpecialCharacters = "\\`*_[]<>|#&~";

    // One line of text made safe for Markdown: whitespace and control characters collapse to single
    // spaces, the text is cut to `maxChars` (with an ellipsis), and every special character is
    // escaped with a backslash. Also makes "://", "www." and "@" inert so nothing turns into an
    // automatic link.
    public static string Inline(string? text, int maxChars)
    {
        var flat = Flatten(text, maxChars, out var cut);
        if (flat.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(flat.Length + 8);
        for (var i = 0; i < flat.Length; i++)
        {
            var c = flat[i];
            if (SpecialCharacters.Contains(c))
            {
                sb.Append('\\').Append(c);
            }
            else if (c == '@')
            {
                sb.Append("\\@");
            }
            else if (c == ':' && i + 2 < flat.Length && flat[i + 1] == '/' && flat[i + 2] == '/')
            {
                sb.Append(":\\/\\/");
                i += 2;
            }
            else if ((c == 'w' || c == 'W')
                && i + 3 < flat.Length
                && string.Compare(flat, i, "www.", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
            {
                sb.Append(flat, i, 3).Append("\\.");
                i += 3;
            }
            else
            {
                sb.Append(c);
            }
        }

        if (cut)
        {
            sb.Append('\u2026');
        }

        return sb.ToString();
    }

    // One line of unescaped text (whitespace collapsed, cut with an ellipsis), for places such as a
    // fenced code block where escaping would show up as stray backslashes.
    public static string Plain(string? text, int maxChars)
    {
        var flat = Flatten(text, maxChars, out var cut);
        return cut ? flat + "…" : flat;
    }

    // The text inside a code span: one line, cut to `maxChars`, wrapped in a backtick run longer than
    // any run inside it, so the content can never close the span early. In a table cell the pipe is
    // escaped as well.
    public static string Code(string? text, int maxChars, bool inTable = false)
    {
        var flat = Flatten(text, maxChars, out var cut);
        if (cut)
        {
            flat += "\u2026";
        }

        if (flat.Length == 0)
        {
            return string.Empty;
        }

        if (inTable)
        {
            flat = flat.Replace("|", "\\|", StringComparison.Ordinal);
        }

        flat = DefuseAngles(flat);
        var fence = new string('`', LongestRun(flat, '`') + 1);
        var pad = flat[0] == '`' || flat[^1] == '`' ? " " : string.Empty;
        return fence + pad + flat + pad + fence;
    }

    // A fenced code block holding `body` (control characters removed, lines kept). The fence is
    // longer than any backtick run in the body, so the body can never close it.
    public static string Fence(string language, string? body)
    {
        var clean = StripControl(body ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim('\n');
        clean = DefuseAngles(clean);
        var fence = new string('`', Math.Max(3, LongestRun(clean, '`') + 1));
        return fence + language + "\n" + clean + "\n" + fence;
    }

    // A YAML double-quoted string (JSON strings are valid YAML), for the front matter.
    public static string YamlString(string? text)
    {
        var flat = Flatten(text, 300, out var cut);
        if (cut)
        {
            flat += "\u2026";
        }

        var sb = new StringBuilder("\"");
        foreach (var c in flat)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '<' or '>' or '[' or ']' or '`':
                    // Written as a YAML unicode escape so a viewer that does not know front matter
                    // still sees no HTML, link or code syntax.
                    sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.Append('"').ToString();
    }

    // Angle brackets inside code (spans and fences) are swapped for look-alike single quotation
    // marks, so no raw HTML-looking text appears anywhere in the document, even where Markdown
    // would treat it as inert code.
    private static string DefuseAngles(string text) =>
        text.Replace('<', '\u2039').Replace('>', '\u203A');

    // Formats a whole number the same way on every machine.
    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    // Whitespace and control characters become single spaces; trimmed; cut to `maxChars` without
    // splitting a surrogate pair. `cut` says whether text was dropped.
    private static string Flatten(string? text, int maxChars, out bool cut)
    {
        cut = false;
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var lastWasSpace = true;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            sb.Append(c);
        }

        var flat = sb.ToString().TrimEnd();
        if (flat.Length > maxChars)
        {
            var end = Math.Max(0, maxChars);
            if (end > 0 && char.IsHighSurrogate(flat[end - 1]))
            {
                end--;
            }

            flat = flat[..end].TrimEnd();
            cut = true;
        }

        return flat;
    }

    // Removes control characters except line feeds and carriage returns.
    private static string StripControl(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c == '\n' || c == '\r' || !char.IsControl(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    // Length of the longest run of `c` in the text.
    private static int LongestRun(string text, char c)
    {
        int best = 0, run = 0;
        foreach (var ch in text)
        {
            run = ch == c ? run + 1 : 0;
            best = Math.Max(best, run);
        }

        return best;
    }
}

