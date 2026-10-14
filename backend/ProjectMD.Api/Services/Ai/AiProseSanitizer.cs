using System.Text;
using System.Text.RegularExpressions;

namespace ProjectMD.Api.Services.Ai;

// Cleans one string from the model's answer before it is cached. The answer is built from untrusted
// page text, so it is treated as untrusted too: raw HTML is removed, images are dropped, links are
// reduced to their text, control characters and newlines become single spaces and the length is
// capped. The result is plain text; the document renderer escapes it again (MarkdownText.Inline)
// when it writes it into the Markdown, so nothing can add headings, lists, table cells, links or
// HTML.
public static partial class AiProseSanitizer
{
    // Anything that looks like an HTML tag or comment.
    [GeneratedRegex(@"<!--.*?-->|</?[a-zA-Z][^>]*>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    // A Markdown image: ![alt](target). Dropped completely.
    [GeneratedRegex(@"!\[[^\]]*\]\((?:[^()]|\([^()]*\))*\)", RegexOptions.CultureInvariant)]
    private static partial Regex ImageRegex();

    // A Markdown link: [text](target). Only the text is kept.
    [GeneratedRegex(@"\[(?<text>[^\]]*)\]\((?:[^()]|\([^()]*\))*\)", RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();

    // Returns one line of plain text, cut to `maxChars` characters, or an empty string when nothing
    // usable is left. Cleaning the result again changes nothing.
    public static string Clean(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var clean = HtmlTagRegex().Replace(text, " ");
        clean = ImageRegex().Replace(clean, " ");
        clean = LinkRegex().Replace(clean, m => m.Groups["text"].Value);

        var sb = new StringBuilder(clean.Length);
        var lastWasSpace = true;
        foreach (var c in clean)
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
            var end = maxChars;
            if (end > 0 && char.IsHighSurrogate(flat[end - 1]))
            {
                end--;
            }

            flat = flat[..end].TrimEnd();
        }

        return flat;
    }
}
