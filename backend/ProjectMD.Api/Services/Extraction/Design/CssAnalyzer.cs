using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProjectMD.Api.Services.Extraction.Design;

// Reads stylesheet text (never stored, only parsed here) for two things the computed styles cannot
// show: the CSS custom properties declared on :root / html, and the pixel breakpoints used in
// @media rules. It is a small careful scanner, not a full CSS parser: it removes comments, walks
// the braces (respecting quotes and parentheses), looks through @media/@supports/@layer wrappers
// and ignores everything else. Garbage in gives fewer results out, never an exception.
public static partial class CssAnalyzer
{
    // The most variables and breakpoints kept, and the longest variable value.
    public const int MaxVariables = 40;
    public const int MaxBreakpoints = 8;
    public const int MaxValueChars = 60;

    // The longest stylesheet (or <style> block) text that is read, and how many are read.
    public const int MaxSheetChars = 256 * 1024;
    public const int MaxSheets = 40;

    // Name fragments that mark a variable as design-relevant; such variables are listed first.
    private static readonly string[] PreferredNameParts =
        ["color", "colour", "bg", "background", "primary", "accent", "text", "foreground", "border", "space", "spacing", "gap", "radius", "rounded", "font", "size"];

    // The variables declared on :root / html, last declaration wins, preferred names first, at most
    // MaxVariables, values cut to MaxValueChars. Variables declared only inside @media or @supports
    // (usually theme overrides such as dark mode) are not listed.
    public static IReadOnlyList<CssVariable> ExtractVariables(IEnumerable<string> sheets)
    {
        var order = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var sheet in sheets)
        {
            var css = StripComments(Cap(sheet));
            foreach (var rule in ReadRules(css, insideCondition: false))
            {
                if (rule.InsideCondition || !TargetsRoot(rule.Prelude))
                {
                    continue;
                }

                foreach (var (name, value) in ReadCustomProperties(rule.Body))
                {
                    if (!values.ContainsKey(name))
                    {
                        order.Add(name);
                    }

                    values[name] = value;
                }
            }
        }

        return order
            .Select((name, index) => (name, index, score: Preference(name)))
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.index)
            .Take(MaxVariables)
            .Select(x => new CssVariable(x.name, values[x.name]))
            .ToList();
    }

    // The distinct pixel widths used in min-width / max-width media conditions (em and rem count as
    // 16px), most frequent first when there are more than MaxBreakpoints, returned as "768px" and
    // sorted from small to large.
    public static IReadOnlyList<string> ExtractBreakpoints(IEnumerable<string> sheets)
    {
        var counts = new Dictionary<int, int>();
        foreach (var sheet in sheets)
        {
            var css = StripComments(Cap(sheet));
            foreach (var rule in ReadRules(css, insideCondition: false))
            {
                if (rule.MediaPrelude is null)
                {
                    continue;
                }

                foreach (var px in WidthsIn(rule.MediaPrelude))
                {
                    counts[px] = counts.GetValueOrDefault(px) + 1;
                }
            }
        }

        return counts
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key)
            .Take(MaxBreakpoints)
            .Select(p => p.Key)
            .Order()
            .Select(px => px.ToString(CultureInfo.InvariantCulture) + "px")
            .ToList();
    }

    // The text of the <style> elements in rendered HTML (inline CSS is a common place for variables),
    // at most MaxSheets blocks of MaxSheetChars each.
    public static IReadOnlyList<string> InlineStyles(string? html)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(html))
        {
            return result;
        }

        foreach (Match match in StyleElementRegex().Matches(html))
        {
            if (result.Count >= MaxSheets)
            {
                break;
            }

            result.Add(Cap(match.Groups[1].Value));
        }

        return result;
    }

    // Cuts a stylesheet to the size limit.
    private static string Cap(string sheet) => sheet.Length > MaxSheetChars ? sheet[..MaxSheetChars] : sheet;

    // Removes /* ... */ comments (an unterminated one removes the rest).
    internal static string StripComments(string css)
    {
        if (!css.Contains("/*", StringComparison.Ordinal))
        {
            return css;
        }

        var sb = new StringBuilder(css.Length);
        var i = 0;
        while (i < css.Length)
        {
            if (css[i] == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                var end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    break;
                }

                i = end + 2;
                sb.Append(' ');
                continue;
            }

            sb.Append(css[i++]);
        }

        return sb.ToString();
    }

    // One rule found by the scanner. Prelude is the text before "{" (selector or at-rule header);
    // Body is the text inside the braces; InsideCondition is true when the rule sits inside a
    // @media/@supports block; MediaPrelude is set for @media blocks themselves.
    private sealed record Rule(string Prelude, string Body, bool InsideCondition, string? MediaPrelude);

    // Walks the braces and returns every style rule and @media header, looking inside @media,
    // @supports, @layer and @container blocks (marking the conditional ones).
    private static IEnumerable<Rule> ReadRules(string css, bool insideCondition)
    {
        var i = 0;
        while (i < css.Length)
        {
            var open = FindTopLevel(css, i, '{');
            var semicolon = FindTopLevel(css, i, ';');
            if (open < 0)
            {
                yield break;
            }

            if (semicolon >= 0 && semicolon < open)
            {
                // A statement such as @import or @charset: skip it.
                i = semicolon + 1;
                continue;
            }

            var close = FindMatchingBrace(css, open);
            var prelude = css[i..open].Trim();
            var body = close < 0 ? css[(open + 1)..] : css[(open + 1)..close];
            i = close < 0 ? css.Length : close + 1;

            if (prelude.StartsWith('@'))
            {
                var name = AtRuleName(prelude);
                if (name == "media")
                {
                    yield return new Rule(prelude, string.Empty, insideCondition, prelude);
                }

                if (name is "media" or "supports" or "container")
                {
                    foreach (var inner in ReadRules(body, insideCondition: true))
                    {
                        yield return inner;
                    }
                }
                else if (name == "layer")
                {
                    foreach (var inner in ReadRules(body, insideCondition))
                    {
                        yield return inner;
                    }
                }

                continue;
            }

            yield return new Rule(prelude, body, insideCondition, null);
        }
    }

    // "@media screen and (...)" -> "media".
    private static string AtRuleName(string prelude)
    {
        var end = 1;
        while (end < prelude.Length && (char.IsLetter(prelude[end]) || prelude[end] == '-'))
        {
            end++;
        }

        return prelude[1..end].ToLowerInvariant();
    }

    // Finds the next occurrence of target that is not inside quotes or parentheses; -1 when none.
    private static int FindTopLevel(string css, int start, char target)
    {
        var depth = 0;
        char quote = '\0';
        for (var i = start; i < css.Length; i++)
        {
            var ch = css[i];
            if (quote != '\0')
            {
                if (ch == '\\')
                {
                    i++;
                }
                else if (ch == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (ch)
            {
                case '"' or '\'':
                    quote = ch;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth = Math.Max(0, depth - 1);
                    break;
                default:
                    if (ch == target && depth == 0)
                    {
                        return i;
                    }

                    break;
            }
        }

        return -1;
    }

    // Finds the "}" that matches the "{" at openIndex, respecting quotes; -1 when unterminated.
    private static int FindMatchingBrace(string css, int openIndex)
    {
        var depth = 0;
        char quote = '\0';
        for (var i = openIndex; i < css.Length; i++)
        {
            var ch = css[i];
            if (quote != '\0')
            {
                if (ch == '\\')
                {
                    i++;
                }
                else if (ch == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
            }
            else if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    // True when one of the comma-separated selectors is exactly ":root" or "html" (a theme
    // selector such as [data-theme=dark] or .dark is not the default root).
    private static bool TargetsRoot(string selectorList) =>
        selectorList.Split(',').Any(s =>
        {
            var selector = s.Trim().ToLowerInvariant();
            return selector is ":root" or "html" or "html:root" or ":root:root";
        });

    // Reads "--name: value" declarations from a rule body, dropping !important and trimming values.
    private static IEnumerable<(string Name, string Value)> ReadCustomProperties(string body)
    {
        var i = 0;
        while (i < body.Length)
        {
            var end = FindTopLevel(body, i, ';');
            var declaration = end < 0 ? body[i..] : body[i..end];
            i = end < 0 ? body.Length : end + 1;

            var colon = declaration.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var name = declaration[..colon].Trim();
            if (!CustomPropertyNameRegex().IsMatch(name))
            {
                continue;
            }

            var value = ImportantRegex().Replace(declaration[(colon + 1)..], string.Empty);
            value = WhitespaceRegex().Replace(value, " ").Trim();
            if (value.Length == 0)
            {
                continue;
            }

            yield return (name, value.Length > MaxValueChars ? value[..MaxValueChars] : value);
        }
    }

    // 1 when the variable name suggests colour, spacing, radius or font, else 0.
    private static int Preference(string name)
    {
        var lower = name.ToLowerInvariant();
        return PreferredNameParts.Any(part => lower.Contains(part, StringComparison.Ordinal)) ? 1 : 0;
    }

    // The pixel widths named by min-width / max-width conditions in an @media header, including
    // the range syntax "(width >= 768px)".
    private static IEnumerable<int> WidthsIn(string prelude)
    {
        foreach (Match m in WidthConditionRegex().Matches(prelude))
        {
            var text = m.Groups["value"].Value;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            var px = m.Groups["unit"].Value.Equals("px", StringComparison.OrdinalIgnoreCase) ? value : value * 16;
            var rounded = (int)Math.Round(px, MidpointRounding.AwayFromZero);
            if (rounded is > 0 and <= 10000)
            {
                yield return rounded;
            }
        }
    }

    [GeneratedRegex(@"<style\b[^>]*>(.*?)</style\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex StyleElementRegex();

    [GeneratedRegex(@"^--[A-Za-z0-9_\-]{1,60}$", RegexOptions.CultureInvariant)]
    private static partial Regex CustomPropertyNameRegex();

    [GeneratedRegex(@"!\s*important", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImportantRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(
        @"(?:(?:min|max)-width\s*:\s*|\bwidth\s*(?:>=|<=|>|<)\s*)(?<value>\d+(?:\.\d+)?)(?<unit>px|em|rem)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex WidthConditionRegex();
}
