using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ProjectMD.Api.Services.Extraction.Components;

namespace ProjectMD.Api.Services.Ai;

// Cleans the HTML and CSS the AI wrote for a component preview. The model answer is untrusted, so
// nothing is passed through: the HTML is rebuilt from an allow-list of tags and attributes, and the
// CSS is rebuilt from an allow-list of properties with plain values only. Scripts, event handlers,
// inline styles, images, links to other sites, url(...), @rules and anything unusual are dropped.
// (The website also shows the result in a sandboxed frame with scripts off and a strict content
// policy; this is the first of those two layers.)
public static partial class ComponentSanitizer
{
    public const int MaxHtmlChars = 4000;
    public const int MaxCssChars = 8000;
    private const int MaxNodes = 80;
    private const int MaxDepth = 8;
    private const int MaxRules = 80;
    private const int MaxTextChars = 80;
    private const int MaxValueChars = 200;

    private static readonly HashSet<string> Tags = new(StringComparer.Ordinal)
    {
        "div", "span", "button", "a", "input", "label", "nav", "ul", "ol", "li", "p", "small", "strong", "em",
        "h1", "h2", "h3", "h4", "h5", "h6", "header", "section", "article", "textarea",
    };

    private static readonly HashSet<string> InputTypes = new(StringComparer.Ordinal)
    {
        "text", "email", "search", "password", "url", "tel", "button", "submit", "checkbox", "radio",
    };

    private static readonly HashSet<string> Pseudos = new(StringComparer.Ordinal)
    {
        "hover", "focus", "active", "disabled", "placeholder", "first-child", "last-child", "focus-visible", "checked", "focus-within",
    };

    private static readonly string[] PropertyPrefixes =
    [
        "border", "padding", "margin", "font", "text-", "outline", "min-", "max-", "flex", "transition", "gap",
        "align-", "justify-", "background", "list-style", "line-", "letter-", "white-", "box-", "overflow",
    ];

    private static readonly HashSet<string> Properties = new(StringComparer.Ordinal)
    {
        "color", "width", "height", "display", "opacity", "cursor", "gap", "transform",
    };

    private static readonly string[] Forbidden =
        ["@", "\\", "<", ">", "url(", "expression", "javascript", "behavior", "binding", "image-set", "var(--"];

    // Cleans one component. Returns null when the kind is unknown or nothing usable is left.
    public static ComponentPreview? Clean(string? kind, string? name, string? html, string? css)
    {
        if (kind is null || !ComponentSample.Kinds.Contains(kind))
        {
            return null;
        }

        var cleanHtml = CleanHtml(html);
        if (cleanHtml.Length == 0)
        {
            return null;
        }

        return new ComponentPreview(kind, PlainText(name, 40) is { Length: > 0 } n ? n : kind, cleanHtml, CleanCss(css));
    }

    // Rebuilds the HTML from allowed tags and attributes; "" when nothing is left.
    public static string CleanHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html) || html.Length > MaxHtmlChars * 2)
        {
            return string.Empty;
        }

        var parser = new HtmlParser();
        var document = parser.ParseDocument("<!doctype html><body></body>");
        var nodes = parser.ParseFragment(html, document.Body!);
        var output = new StringBuilder();
        var budget = MaxNodes;
        foreach (var node in nodes)
        {
            Write(node, output, 0, ref budget);
        }

        var text = output.ToString().Trim();
        return text.Length > MaxHtmlChars ? string.Empty : text;
    }

    private static void Write(INode node, StringBuilder output, int depth, ref int budget)
    {
        if (budget <= 0 || depth > MaxDepth)
        {
            return;
        }

        if (node is IText text)
        {
            var value = PlainText(text.Data, MaxTextChars);
            if (value.Length > 0)
            {
                budget--;
                output.Append(WebUtility.HtmlEncode(value));
            }

            return;
        }

        if (node is not IElement element || !Tags.Contains(element.LocalName))
        {
            return;
        }

        budget--;
        var tag = element.LocalName;
        output.Append('<').Append(tag);
        AppendAttributes(element, output);
        output.Append('>');
        if (tag == "input")
        {
            return;
        }

        foreach (var child in element.ChildNodes)
        {
            Write(child, output, depth + 1, ref budget);
        }

        output.Append("</").Append(tag).Append('>');
    }

    private static void AppendAttributes(IElement element, StringBuilder output)
    {
        var classes = (element.GetAttribute("class") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(c => c.Length <= 40 && ClassName().IsMatch(c))
            .Take(6)
            .ToList();
        if (classes.Count > 0)
        {
            output.Append(" class=\"").Append(string.Join(' ', classes)).Append('"');
        }

        if (element.LocalName == "a")
        {
            output.Append(" href=\"#\"");
        }

        if (element.LocalName == "input" && element.GetAttribute("type") is { } type && InputTypes.Contains(type))
        {
            output.Append(" type=\"").Append(type).Append('"');
        }

        AppendText(element, "placeholder", output);
        AppendText(element, "aria-label", output);
        if (element.LocalName == "input")
        {
            AppendText(element, "value", output);
        }

        foreach (var flag in new[] { "disabled", "checked" })
        {
            if (element.HasAttribute(flag))
            {
                output.Append(' ').Append(flag);
            }
        }
    }

    private static void AppendText(IElement element, string attribute, StringBuilder output)
    {
        var value = PlainText(element.GetAttribute(attribute), 60);
        if (value.Length > 0)
        {
            output.Append(' ').Append(attribute).Append("=\"").Append(WebUtility.HtmlEncode(value)).Append('"');
        }
    }

    // Rebuilds the CSS from allowed rules; "" when nothing is left.
    public static string CleanCss(string? css)
    {
        if (string.IsNullOrWhiteSpace(css) || css.Length > MaxCssChars * 2)
        {
            return string.Empty;
        }

        var output = new StringBuilder();
        var rules = 0;
        foreach (Match rule in RuleRegex().Matches(CommentRegex().Replace(css, " ")))
        {
            if (rules >= MaxRules)
            {
                break;
            }

            var selector = Regex.Replace(rule.Groups[1].Value.Trim(), @"\s+", " ");
            var declarations = CleanDeclarations(rule.Groups[2].Value);
            if (declarations.Length == 0 || !SelectorOk(selector))
            {
                continue;
            }

            output.Append(selector).Append(" { ").Append(declarations).Append(" }\n");
            rules++;
        }

        var text = output.ToString().Trim();
        return text.Length > MaxCssChars ? string.Empty : text;
    }

    private static bool SelectorOk(string selector)
    {
        if (selector.Length is 0 or > 120 || !SelectorChars().IsMatch(selector))
        {
            return false;
        }

        return PseudoRegex().Matches(selector).All(m => Pseudos.Contains(m.Groups[1].Value.ToLowerInvariant()));
    }

    private static string CleanDeclarations(string body)
    {
        var kept = new List<string>();
        foreach (var part in body.Split(';'))
        {
            var colon = part.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var property = part[..colon].Trim().ToLowerInvariant();
            var value = part[(colon + 1)..].Trim();
            if (PropertyOk(property) && ValueOk(value))
            {
                kept.Add(property + ": " + value + ";");
            }
        }

        return string.Join(' ', kept);
    }

    private static bool PropertyOk(string property) =>
        property.Length <= 40
        && PropertyName().IsMatch(property)
        && (Properties.Contains(property) || PropertyPrefixes.Any(p => property.StartsWith(p, StringComparison.Ordinal)));

    private static bool ValueOk(string value)
    {
        if (value.Length is 0 or > MaxValueChars || !ValueChars().IsMatch(value))
        {
            return false;
        }

        return !Forbidden.Any(f => value.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    // Plain text: control characters and angle brackets removed, cut to a length.
    internal static string PlainText(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var clean = new string(value.Where(c => !char.IsControl(c) && c != '<' && c != '>').ToArray());
        clean = Regex.Replace(clean, @"\s+", " ").Trim();
        return clean.Length > max ? clean[..max] : clean;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]+$")]
    private static partial Regex ClassName();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"([^{}]+)\{([^{}]*)\}")]
    private static partial Regex RuleRegex();

    [GeneratedRegex(@"^[A-Za-z0-9_\-\.\s,>+~:*]+$")]
    private static partial Regex SelectorChars();

    [GeneratedRegex(@"::?([A-Za-z-]+)")]
    private static partial Regex PseudoRegex();

    [GeneratedRegex(@"^[a-z-]+$")]
    private static partial Regex PropertyName();

    [GeneratedRegex(@"^[\w\s#%,.()\-+/""'!*]+$")]
    private static partial Regex ValueChars();
}
