using System.Text;
using System.Text.RegularExpressions;

namespace ProjectMD.Api.Services.Ai;

// Cleans the model's answer before it is cached or put into a scan document. The answer is built
// from untrusted page text, so it is treated as untrusted too: raw HTML, scripts and unsafe links
// are removed, headings are pushed down so they cannot break the document structure, and the
// length is capped.
public static partial class AiProseSanitizer
{
    // The most characters of prose kept (the output token cap already keeps answers well below this).
    public const int MaxChars = 6000;

    // The heading under which the prose appears in the scan document.
    private const int ProseHeadingLevel = 3;

    // Anything that looks like an HTML tag or comment.
    [GeneratedRegex(@"<!--.*?-->|</?[a-zA-Z][^>]*>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    // A Markdown link or image: [text](target) / ![alt](target).
    [GeneratedRegex(@"!?\[(?<text>[^\]]*)\]\((?<target>(?:[^()]|\([^()]*\))*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();

    // A leading run of # characters at the start of a line (a heading).
    [GeneratedRegex(@"^(?<hashes>#{1,6})(?=\s)", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingRegex();

    // Returns clean Markdown prose, or an empty string when nothing usable is left. Steps: strip a
    // wrapping code fence, remove HTML, replace every link by its plain text (links are not wanted
    // and could point anywhere), drop control characters, demote headings to level 3 or deeper,
    // trim blank lines and cap the length at a whole line.
    public static string Clean(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return string.Empty;
        }

        var text = answer.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        text = StripOuterFence(text);
        text = HtmlTagRegex().Replace(text, string.Empty);
        text = LinkRegex().Replace(text, m => m.Value.StartsWith('!') ? string.Empty : m.Groups["text"].Value);

        var lines = new List<string>();
        var blank = true;
        foreach (var raw in text.Split('\n'))
        {
            var line = StripControl(raw).TrimEnd();
            line = HeadingRegex().Replace(line, m => new string('#', Math.Max(ProseHeadingLevel, m.Groups["hashes"].Length)));
            if (line.Length == 0)
            {
                if (!blank)
                {
                    lines.Add(string.Empty);
                }

                blank = true;
                continue;
            }

            blank = false;
            lines.Add(line);
        }

        var cleaned = string.Join("\n", lines).Trim();
        if (cleaned.Length <= MaxChars)
        {
            return cleaned;
        }

        var cut = cleaned.LastIndexOf('\n', MaxChars - 1);
        return cut > 0 ? cleaned[..cut].TrimEnd() : cleaned[..MaxChars].TrimEnd();
    }

    // If the whole answer is wrapped in one ``` fence, returns what is inside it.
    private static string StripOuterFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal) || !text.EndsWith("```", StringComparison.Ordinal) || text.Length < 6)
        {
            return text;
        }

        var firstBreak = text.IndexOf('\n');
        if (firstBreak < 0)
        {
            return text;
        }

        return text[(firstBreak + 1)..^3].Trim();
    }

    // Removes control characters (tabs become spaces) from one line.
    private static string StripControl(string line)
    {
        var sb = new StringBuilder(line.Length);
        foreach (var c in line)
        {
            if (c == '\t')
            {
                sb.Append(' ');
            }
            else if (!char.IsControl(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
