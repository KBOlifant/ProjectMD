using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction.Components;

namespace ProjectMD.Api.Tests.Ai;

// Tests for the component previews: the sanitiser that cleans the generated HTML and CSS, the reader
// of the in-page capture, and the builder that turns captured element trees into HTML and CSS.
public class ComponentPreviewTests
{
    // ---- Sanitiser --------------------------------------------------------------------------------

    // Scripts, event handlers, inline styles and unknown tags never survive.
    [Theory]
    [InlineData("<button class=\"btn\" onclick=\"alert(1)\">Go</button>", "<button class=\"btn\">Go</button>")]
    [InlineData("<div><script>alert(1)</script><p>Hi</p></div>", "<div><p>Hi</p></div>")]
    [InlineData("<button style=\"color:red\">Go</button>", "<button>Go</button>")]
    [InlineData("<a href=\"https://evil.example/x\">Go</a>", "<a href=\"#\">Go</a>")]
    [InlineData("<a href=\"javascript:alert(1)\">Go</a>", "<a href=\"#\">Go</a>")]
    [InlineData("<div><iframe src=\"https://evil.example\"></iframe><span>ok</span></div>", "<div><span>ok</span></div>")]
    [InlineData("<div><img src=x onerror=alert(1)><span>ok</span></div>", "<div><span>ok</span></div>")]
    [InlineData("<div class=\"a b\" id=\"x\" data-x=\"1\">t</div>", "<div class=\"a b\">t</div>")]
    public void CleanHtml_keeps_only_safe_markup(string input, string expected)
    {
        Assert.Equal(expected, ComponentSanitizer.CleanHtml(input));
    }

    // Text is escaped and an input keeps only safe attributes.
    [Fact]
    public void CleanHtml_escapes_text_and_limits_input_attributes()
    {
        var html = ComponentSanitizer.CleanHtml("<input type=\"text\" placeholder=\"Search\" name=\"q\" onfocus=\"x()\"><p>a &amp; b</p>");

        Assert.Equal("<input type=\"text\" placeholder=\"Search\"><p>a &amp; b</p>", html);
    }

    // Empty and oversized markup give nothing.
    [Fact]
    public void CleanHtml_returns_empty_for_empty_or_huge_input()
    {
        Assert.Equal(string.Empty, ComponentSanitizer.CleanHtml("  "));
        Assert.Equal(string.Empty, ComponentSanitizer.CleanHtml(new string('a', ComponentSanitizer.MaxHtmlChars * 3)));
    }

    // Ordinary styling passes through unchanged.
    [Fact]
    public void CleanCss_keeps_plain_rules_and_states()
    {
        var css = ComponentSanitizer.CleanCss(".btn { color: #fff; background-color: #111827; border-radius: 8px; padding: 8px 16px; } .btn:hover { background-color: #1f2937; }");

        Assert.Contains(".btn { color: #fff; background-color: #111827; border-radius: 8px; padding: 8px 16px; }", css);
        Assert.Contains(".btn:hover { background-color: #1f2937; }", css);
    }

    // Anything that can load something, run something or reach outside is dropped.
    [Theory]
    [InlineData(".a { background: url(https://evil.example/x.png); }")]
    [InlineData(".a { background-image: url('x'); }")]
    [InlineData("@import 'https://evil.example/x.css'; .a { color: red; behavior: x; }")]
    [InlineData(".a { width: expression(alert(1)); }")]
    [InlineData(".a { color: red; } @media print { .b { content: 'x'; } }")]
    [InlineData(".a { position: fixed; }")]
    [InlineData(".a { content: \"x\"; }")]
    [InlineData(".a::before { color: red; }")]
    [InlineData("*[onclick] { color: red; }")]
    public void CleanCss_drops_dangerous_or_unknown_rules(string input)
    {
        var css = ComponentSanitizer.CleanCss(input);

        Assert.DoesNotContain("url(", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", css);
        Assert.DoesNotContain("expression", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("position", css);
        Assert.DoesNotContain("content", css);
        Assert.DoesNotContain("onclick", css);
        Assert.DoesNotContain("::before", css);
    }

    // A good rule survives next to a bad declaration in the same block.
    [Fact]
    public void CleanCss_drops_only_the_bad_declaration()
    {
        var css = ComponentSanitizer.CleanCss(".a { color: red; background: url(x); }");

        Assert.Equal(".a { color: red; }", css);
    }

    // ---- Capture reader ---------------------------------------------------------------------------

    private const string CapturedButton = """
        {"items":[
          {"kind":"button","background":"#0B1220","height":40,"root":{"t":"button","x":true,
            "s":{"color":"#ffffff","background-color":"#111827","evil":"x","border-top-left-radius":"8px"},"c":[]}},
          {"kind":"script","background":"#fff","root":{"t":"button","s":{"color":"red"},"c":[]}},
          {"kind":"card","root":{"t":"iframe","s":{"color":"red"},"c":[]}}
        ]}
        """;

    // The capture reader keeps known kinds, tags and properties only.
    [Fact]
    public void Sample_keeps_known_kinds_tags_and_properties()
    {
        var styles = ComponentSample.TryParse(CapturedButton);

        var item = Assert.Single(styles!.Items);
        Assert.Equal("button", item.Kind);
        Assert.Equal("#0b1220", item.Background);
        Assert.Equal(40, item.Height);
        Assert.True(item.Root.HasText);
        Assert.Equal(3, item.Root.Styles.Count);
        Assert.DoesNotContain("evil", item.Root.Styles.Keys);
    }

    // Trees are cut to a sane depth.
    [Fact]
    public void Sample_limits_the_depth_of_the_tree()
    {
        var json = "{\"items\":[{\"kind\":\"card\",\"root\":" + Nested(12) + "}]}";

        var root = ComponentSample.TryParse(json)!.Items[0].Root;

        var depth = 0;
        for (var node = root; node.Children.Count > 0; node = node.Children[0])
        {
            depth++;
        }

        Assert.True(depth <= 5, $"depth was {depth}");
    }

    private static string Nested(int levels) =>
        levels == 0 ? "{\"t\":\"div\",\"s\":{},\"c\":[]}" : "{\"t\":\"div\",\"s\":{},\"c\":[" + Nested(levels - 1) + "]}";

    // Nothing usable gives null.
    [Theory]
    [InlineData(null)]
    [InlineData("nope")]
    [InlineData("{\"items\":[]}")]
    [InlineData("{\"other\":1}")]
    public void Sample_returns_null_for_unusable_input(string? json)
    {
        Assert.Null(ComponentSample.TryParse(json));
    }

    // ---- Builder ----------------------------------------------------------------------------------

    private static ComponentNode Node(string tag, Dictionary<string, string>? styles = null, bool text = false, params ComponentNode[] children) =>
        new(tag, styles ?? [], null, text, children);

    private static ComponentStyles Capture(string kind, ComponentNode root, string background = "#ffffff", int height = 40) =>
        new([new ComponentSnapshot(kind, background, height, root)]);

    // A button keeps its look: colours, corners, padding, font; defaults are left out; text is a sample word.
    [Fact]
    public void Builder_writes_a_button_with_its_measured_style()
    {
        var root = Node(
            "button",
            new()
            {
                ["display"] = "inline-block", ["color"] = "#ffffff", ["background-color"] = "#111827",
                ["border-top-left-radius"] = "8px", ["border-top-right-radius"] = "8px",
                ["border-bottom-right-radius"] = "8px", ["border-bottom-left-radius"] = "8px",
                ["padding-top"] = "10px", ["padding-bottom"] = "10px", ["padding-left"] = "20px", ["padding-right"] = "20px",
                ["font-size"] = "14px", ["font-weight"] = "600", ["box-shadow"] = "none", ["letter-spacing"] = "normal",
                ["border-top-style"] = "none", ["border-top-width"] = "0px",
            },
            text: true);

        var preview = ComponentPreviewBuilder.Build(Capture("button", root, "#0b1220", 44))!.Items[0];

        Assert.Equal("<button class=\"c1\">Button</button>", preview.Html);
        Assert.Contains("background-color: #111827;", preview.Css);
        Assert.Contains("border-radius: 8px;", preview.Css);
        Assert.Contains("padding: 10px 20px 10px 20px;", preview.Css);
        Assert.Contains("font-weight: 600;", preview.Css);
        Assert.DoesNotContain("box-shadow", preview.Css);
        Assert.DoesNotContain("border:", preview.Css);
        Assert.Equal("#0b1220", preview.Background);
        Assert.Equal(44, preview.Height);
        Assert.Equal("Button", preview.Name);
    }

    // Nesting is kept and identical child styles share one class.
    [Fact]
    public void Builder_keeps_nesting_and_shares_classes()
    {
        var item = new Dictionary<string, string> { ["color"] = "#334155", ["font-size"] = "14px" };
        var root = Node(
            "div",
            new() { ["display"] = "block", ["border-top-style"] = "solid", ["border-top-width"] = "1px", ["border-top-color"] = "#e2e8f0",
                    ["border-right-style"] = "solid", ["border-right-width"] = "1px", ["border-right-color"] = "#e2e8f0",
                    ["border-bottom-style"] = "solid", ["border-bottom-width"] = "1px", ["border-bottom-color"] = "#e2e8f0",
                    ["border-left-style"] = "solid", ["border-left-width"] = "1px", ["border-left-color"] = "#e2e8f0" },
            false,
            Node("h3", new(item), true),
            Node("p", new(item), true));

        var preview = ComponentPreviewBuilder.Build(Capture("card", root))!.Items[0];

        Assert.Contains("border: 1px solid #e2e8f0;", preview.Css);
        Assert.Equal(1, preview.Css.Split("color: #334155;").Length - 1);
        Assert.Matches("<div class=\"c1\"><h3 class=\"c2\">Card title</h3><p class=\"c2\">A short line of sample text.</p></div>", preview.Html);
    }

    // Two examples of a kind get numbered names; nothing captured gives null.
    [Fact]
    public void Builder_numbers_repeated_kinds_and_handles_empty_input()
    {
        var root = Node("button", new() { ["color"] = "#fff" }, true);
        var captured = new ComponentStyles([
            new ComponentSnapshot("button", "#fff", 40, root),
            new ComponentSnapshot("button", "#fff", 40, root),
        ]);

        var previews = ComponentPreviewBuilder.Build(captured)!;

        Assert.Equal(["Button", "Button 2"], previews.Items.Select(i => i.Name));
        Assert.Null(ComponentPreviewBuilder.Build(null));
    }

    // Whatever the page sends, the result passes the sanitiser: no url(), no scripts.
    [Fact]
    public void Builder_output_is_always_sanitised()
    {
        var root = Node("button", new() { ["color"] = "red", ["font-family"] = "x; background: url(https://evil.example/a.png)" }, true);

        var preview = ComponentPreviewBuilder.Build(Capture("button", root))!.Items[0];

        Assert.DoesNotContain("url(", preview.Css);
        Assert.DoesNotContain("evil", preview.Css);
    }
}
