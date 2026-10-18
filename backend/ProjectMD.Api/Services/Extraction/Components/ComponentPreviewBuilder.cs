using System.Net;
using System.Text;
using ProjectMD.Api.Services.Ai;

namespace ProjectMD.Api.Services.Extraction.Components;

// Turns the captured component trees into HTML and CSS that look like the originals: every element
// keeps its tag, nesting and measured styles, repeated style sets share one class, and values that only
// state a default are left out. No AI and no network: the text inside elements is a fixed sample word
// (the page's own text is never captured), and everything passes through ComponentSanitizer at the end.
public static class ComponentPreviewBuilder
{
    // The most characters of HTML and CSS kept for all previews of one scan together.
    private const int MaxTotalChars = 40_000;

    private const string Reset = "* { box-sizing: border-box; margin: 0; }";

    private static readonly string[] Sides = ["top", "right", "bottom", "left"];
    private static readonly string[] Corners = ["top-left", "top-right", "bottom-right", "bottom-left"];

    // Property values that are the browser default and need not be written.
    private static readonly Dictionary<string, string[]> Defaults = new(StringComparer.Ordinal)
    {
        ["box-shadow"] = ["none"],
        ["font-style"] = ["normal"],
        ["line-height"] = ["normal"],
        ["letter-spacing"] = ["normal", "0px"],
        ["text-transform"] = ["none"],
        ["text-align"] = ["start", "left"],
        ["text-decoration-line"] = ["none"],
        ["flex-direction"] = ["row"],
        ["flex-wrap"] = ["nowrap"],
        ["align-items"] = ["normal"],
        ["justify-content"] = ["normal"],
        ["gap"] = ["normal", "0px", "normal normal"],
        ["opacity"] = ["1"],
        ["cursor"] = ["auto"],
        ["white-space"] = ["normal"],
        ["background-color"] = ["transparent"],
    };

    private static readonly HashSet<string> BlockTags = new(StringComparer.Ordinal)
    {
        "div", "p", "section", "nav", "header", "article", "ul", "ol", "h1", "h2", "h3", "h4", "h5", "h6",
    };

    private static readonly HashSet<string> InlineTags = new(StringComparer.Ordinal)
    {
        "span", "a", "strong", "em", "small", "label",
    };

    // Builds the previews, or null when nothing was captured or nothing survived cleaning.
    public static ComponentPreviews? Build(ComponentStyles? captured)
    {
        if (captured is null)
        {
            return null;
        }

        var previews = new List<ComponentPreview>();
        var perKind = new Dictionary<string, int>(StringComparer.Ordinal);
        var total = 0;
        foreach (var snapshot in captured.Items)
        {
            perKind[snapshot.Kind] = perKind.GetValueOrDefault(snapshot.Kind) + 1;
            var (html, css) = Render(snapshot);
            var name = perKind[snapshot.Kind] > 1 ? Title(snapshot.Kind) + " " + perKind[snapshot.Kind] : Title(snapshot.Kind);
            if (ComponentSanitizer.Clean(snapshot.Kind, name, html, css) is not { } clean)
            {
                continue;
            }

            total += clean.Html.Length + clean.Css.Length;
            if (total > MaxTotalChars)
            {
                break;
            }

            previews.Add(clean with { Background = snapshot.Background, Height = snapshot.Height });
        }

        return previews.Count == 0 ? null : new ComponentPreviews(previews);
    }

    private static (string Html, string Css) Render(ComponentSnapshot snapshot)
    {
        var classes = new Dictionary<string, string>(StringComparer.Ordinal);
        var html = new StringBuilder();
        WriteNode(snapshot.Root, snapshot.Kind, isRoot: true, classes, html);

        var css = new StringBuilder(Reset).Append('\n');
        foreach (var (declarations, name) in classes)
        {
            css.Append('.').Append(name).Append(" { ").Append(declarations).Append(" }\n");
        }

        return (html.ToString(), css.ToString());
    }

    private static void WriteNode(ComponentNode node, string kind, bool isRoot, Dictionary<string, string> classes, StringBuilder html)
    {
        var declarations = Declarations(node, isRoot);
        string? className = null;
        if (declarations.Length > 0)
        {
            if (!classes.TryGetValue(declarations, out className))
            {
                className = "c" + (classes.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                classes[declarations] = className;
            }
        }

        html.Append('<').Append(node.Tag);
        if (className is not null)
        {
            html.Append(" class=\"").Append(className).Append('"');
        }

        if (node.Tag is "input" or "textarea")
        {
            if (node.Tag == "input")
            {
                html.Append(" type=\"").Append(node.InputType ?? "text").Append('"');
            }

            html.Append(" placeholder=\"").Append(WebUtility.HtmlEncode(SampleText(node, kind))).Append("\">");
            if (node.Tag == "textarea")
            {
                html.Append("</textarea>");
            }

            return;
        }

        html.Append('>');
        if (node.HasText)
        {
            html.Append(WebUtility.HtmlEncode(SampleText(node, kind)));
        }

        foreach (var child in node.Children)
        {
            WriteNode(child, kind, isRoot: false, classes, html);
        }

        html.Append("</").Append(node.Tag).Append('>');
    }

    // The CSS declarations for one element: shorthands where the four sides agree, defaults left out.
    internal static string Declarations(ComponentNode node, bool isRoot)
    {
        var s = node.Styles;
        var parts = new List<string>();

        string? Get(string name) => s.TryGetValue(name, out var value) ? value : null;
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(name + ": " + value + ";");
            }
        }

        if (Get("display") is { } display && display != DefaultDisplay(node.Tag))
        {
            Add("display", display);
        }

        Add("color", Get("color"));
        foreach (var name in Defaults.Keys)
        {
            if (Get(name) is { } value && !Defaults[name].Contains(value))
            {
                Add(name, value);
            }
        }

        Add("font-family", Get("font-family"));
        Add("font-size", Get("font-size"));
        Add("font-weight", Get("font-weight"));

        AddBorders(s, parts);
        AddRadius(s, parts);
        AddBox("padding", s, parts);
        if (!isRoot)
        {
            AddBox("margin", s, parts);
        }

        Add("width", Get("width"));
        Add("height", Get("height"));
        return string.Join(' ', parts);
    }

    private static string DefaultDisplay(string tag) =>
        BlockTags.Contains(tag) ? "block"
        : InlineTags.Contains(tag) ? "inline"
        : tag is "li" ? "list-item"
        : "inline-block";

    // "border: 1px solid #ccc" when all four sides agree, otherwise one rule per visible side.
    private static void AddBorders(IReadOnlyDictionary<string, string> s, List<string> parts)
    {
        string Side(string side) =>
            s.TryGetValue("border-" + side + "-style", out var style) && style is not ("none" or "hidden")
            && s.TryGetValue("border-" + side + "-width", out var width) && width != "0px"
                ? width + " " + style + " " + (s.GetValueOrDefault("border-" + side + "-color") ?? "currentcolor")
                : string.Empty;

        var values = Sides.Select(Side).ToArray();
        if (values.All(v => v.Length == 0))
        {
            return;
        }

        if (values.Distinct().Count() == 1)
        {
            parts.Add("border: " + values[0] + ";");
            return;
        }

        for (var i = 0; i < Sides.Length; i++)
        {
            if (values[i].Length > 0)
            {
                parts.Add("border-" + Sides[i] + ": " + values[i] + ";");
            }
        }
    }

    private static void AddRadius(IReadOnlyDictionary<string, string> s, List<string> parts)
    {
        var values = Corners.Select(c => s.GetValueOrDefault("border-" + c + "-radius") ?? "0px").ToArray();
        if (values.All(v => v == "0px"))
        {
            return;
        }

        parts.Add("border-radius: " + (values.Distinct().Count() == 1 ? values[0] : string.Join(' ', values)) + ";");
    }

    // padding / margin as one shorthand (top right bottom left), left out when all four are zero.
    private static void AddBox(string name, IReadOnlyDictionary<string, string> s, List<string> parts)
    {
        var values = Sides.Select(side => s.GetValueOrDefault(name + "-" + side) ?? "0px").ToArray();
        if (values.All(v => v == "0px"))
        {
            return;
        }

        parts.Add(name + ": " + (values.Distinct().Count() == 1 ? values[0] : string.Join(' ', values)) + ";");
    }

    // A fixed sample word for the text of an element, chosen by tag and kind.
    internal static string SampleText(ComponentNode node, string kind) => node.Tag switch
    {
        "input" or "textarea" => kind == "input" ? "Search" : "Type here",
        "button" => "Button",
        "a" => kind == "nav" ? "Link" : "Button",
        "h1" or "h2" => "Heading",
        "h3" or "h4" or "h5" or "h6" => "Card title",
        "p" => "A short line of sample text.",
        "li" => "Item",
        "label" => "Label",
        _ => kind == "badge" ? "Badge" : kind == "nav" ? "Link" : kind == "heading" ? "Heading" : "Text",
    };

    private static string Title(string kind) => char.ToUpperInvariant(kind[0]) + kind[1..];
}
