using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace ProjectMD.Api.Services.Extraction;

// Turns the rendered HTML of a page into a small PageDigest, with no AI and no network. A typical
// page shrinks by well over 90% (scripts, styles, SVG and markup are what weigh most).
//
// Library choice: AngleSharp, not HtmlAgilityPack. AngleSharp implements the HTML5 parsing
// algorithm that browsers use, so it builds the same tree for broken markup that the page showed
// (which matters here because we read the browser's own DOM dump); HtmlAgilityPack is a more
// forgiving "bag of tags" parser. AngleSharp is also pure managed code with no native parts.
//
// The output has no HTML at all, so the "keep only href, alt, title, lang and meta attributes" rule
// is met by construction: we read those attributes and never copy any other.
//
// What happens, in order: (1) read the metadata and the markers from the full document; (2) delete
// noise (scripts, styles, svg, iframes, hidden elements, cookie banners); (3) pick the main content
// area; (4) walk it in document order into text blocks, dropping duplicates, tiny lines and
// cookie-banner phrases; (5) collect the heading outline and the links; (6) cut the text to the
// budget by priority; (7) hash the normalised result.
public sealed partial class HtmlReducer : IHtmlReducer
{
    // Elements removed outright.
    private const string NoiseSelector =
        "script,style,noscript,svg,canvas,iframe,frame,frameset,template,object,embed,audio,video,source,track," +
        "picture,map,link,meta,base,select,option,datalist,input,textarea,dialog:not([open])";

    // Tags that start a new line of text when walking the tree.
    private static readonly HashSet<string> BlockTags = new(StringComparer.Ordinal)
    {
        "address", "article", "aside", "blockquote", "body", "button", "caption", "dd", "details", "div", "dl", "dt",
        "fieldset", "figcaption", "figure", "footer", "form", "header", "hr", "li", "main", "nav", "ol", "p", "pre",
        "section", "summary", "table", "tbody", "td", "tfoot", "th", "thead", "tr", "ul", "dialog", "menu", "legend",
    };

    // Headings: their text goes to the outline, not into the body text.
    private static readonly HashSet<string> HeadingTags = new(StringComparer.Ordinal)
    {
        "h1", "h2", "h3", "h4", "h5", "h6",
    };

    // Roles whose content is page furniture rather than the page's own content.
    private static readonly HashSet<string> FurnitureRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "navigation", "banner", "contentinfo", "complementary",
    };

    // Lines containing one of these (and short enough) are cookie or consent banners.
    private static readonly string[] CookiePhrases =
    [
        "we use cookies", "this website uses cookies", "this site uses cookies", "uses cookies", "use of cookies",
        "accept all cookies", "accept cookies", "allow all cookies", "allow cookies", "reject all cookies",
        "reject all", "cookie settings", "cookie preferences", "cookie policy", "manage cookies",
        "manage preferences", "manage consent", "privacy settings", "your privacy choices", "do not sell",
        "strictly necessary cookies", "by continuing to browse", "by continuing to use this site",
        "by using this site, you agree", "consent to the use",
    ];

    // Class or id words that mark a cookie or consent banner container.
    [GeneratedRegex(@"cookie|consent|gdpr|onetrust|cookiebot|cc-window|cc-banner|trustarc|osano|didomi|usercentrics|termly",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 250)]
    private static partial Regex BannerWords();

    // Inline styles that hide an element (display:none only; visibility is left alone because some
    // pages set it before an animation reveals real content).
    [GeneratedRegex(@"display\s*:\s*none", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 250)]
    private static partial Regex HiddenStyle();

    // A "data:" URI or a run of base64 that slipped into text.
    [GeneratedRegex(@"data:[a-z]+/[\w.+-]+;base64,[A-Za-z0-9+/=]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 250)]
    private static partial Regex DataUri();

    // The main area must have at least this much text to be preferred over the whole body.
    private const int MinMainChars = 200;

    // The longest single block of text kept.
    private const int MaxBlockChars = 1500;

    // A word longer than this (a URL, a token, base64) is junk in running text and is dropped.
    private const int MaxWordChars = 120;

    // Nesting deeper than this is flattened instead of walked (protects against hostile markup).
    private const int MaxDepth = 256;

    // JSON-LD limits: how many blocks, how long one may be after compacting.
    private const int MaxJsonLdBlocks = 6;

    private const int MaxJsonLdChars = 3000;

    private const int MaxListedUrls = 200;

    private const int MaxUrlChars = 500;

    // Reduces the page. Pure and deterministic: the same input gives the same digest.
    public PageDigest Reduce(RawPage page, ReduceOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);

        var html = page.Html ?? string.Empty;
        var document = new HtmlParser().ParseDocument(html);
        var baseUri = ResolveBase(document, page.FinalUrl);

        // 1. Metadata and markers come from the untouched document.
        var (title, description, language) = ReadMetadata(document);
        var jsonLd = ReadJsonLd(document);
        var markers = MarkerExtractor.Extract(document);
        var scriptUrls = MergeUrls(page.ScriptUrls, DomUrls(document, "script[src]", "src", baseUri));
        var stylesheetUrls = MergeUrls(page.StylesheetUrls, DomUrls(document, "link[rel~='stylesheet'][href]", "href", baseUri));

        // 2. Delete noise.
        RemoveNoise(document);

        var body = document.Body ?? document.DocumentElement;
        if (body is null)
        {
            return Assemble(page, title, description, language, [], string.Empty, [], scriptUrls, stylesheetUrls, jsonLd, markers, html.Length);
        }

        // 3 and 4. Main content area, then text blocks in document order.
        var root = ChooseRoot(document, body);
        var walker = new Walker(startInContent: !ReferenceEquals(root, body));
        walker.WalkChildren(root, 0);
        var text = SelectText(walker.Blocks, options.MaxTextChars);

        // 5. Outline and links.
        var outline = BuildOutline(body, options.MaxHeadings);
        var links = BuildLinks(body, baseUri, page.FinalUrl, options.MaxLinks);

        return Assemble(page, title, description, language, outline, text, links, scriptUrls, stylesheetUrls, jsonLd, markers, html.Length);
    }

    // Builds the digest: sizes, token estimate and content hash.
    private static PageDigest Assemble(
        RawPage page,
        string? title,
        string? description,
        string? language,
        IReadOnlyList<HeadingNode> outline,
        string text,
        IReadOnlyList<LinkRef> links,
        IReadOnlyList<string> scriptUrls,
        IReadOnlyList<string> stylesheetUrls,
        IReadOnlyList<string> jsonLd,
        IReadOnlyList<string> markers,
        int rawChars)
    {
        var outlineText = string.Join("\n", outline.Select(h => new string('#', h.Level) + " " + h.Text));

        // Everything that would be sent on to a reader (or an AI), measured in characters.
        var reducedChars =
            (title?.Length ?? 0) + (description?.Length ?? 0) + outlineText.Length + text.Length +
            links.Sum(l => l.Text.Length + l.Href.Length + 2) + jsonLd.Sum(j => j.Length);

        // The hash covers only the stable human-facing content, scrubbed of volatile bits.
        var hashInput = string.Join(
            "\n", "title:" + title, "meta:" + description, "outline:", outlineText, "text:", text);
        // Captured API calls and design system (see CaptureHash): empty when nothing was captured, so
        // such pages keep the hash they always had. Added after scrubbing so templates stay intact.
        var captureForm = CaptureHash.Canonical(page.Api, page.Design);
        var contentHash = TextTools.Sha256Hex(TextTools.ScrubVolatile(hashInput) + (captureForm.Length == 0 ? string.Empty : "\ncapture:\n" + captureForm));

        // A case-insensitive copy of the headers (written one by one so two spellings of the same
        // name cannot throw).
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in page.Headers)
        {
            headers[name] = value;
        }

        return new PageDigest(
            page.FinalUrl,
            page.StatusCode,
            title,
            description,
            language,
            outline,
            text,
            links,
            scriptUrls,
            stylesheetUrls,
            headers,
            jsonLd,
            rawChars,
            reducedChars,
            TextTools.EstimateTokens(reducedChars),
            contentHash,
            markers,
            page.Api,
            page.Design,
            page.Visual);
    }

    // ---- Metadata ---------------------------------------------------------------------------------

    // The base address for relative links: <base href> when it is a valid http(s) URL, else the page URL.
    private static Uri ResolveBase(IDocument document, Uri finalUrl)
    {
        var href = document.QuerySelector("base[href]")?.GetAttribute("href");
        if (!string.IsNullOrWhiteSpace(href) &&
            Uri.TryCreate(finalUrl, href.Trim(), out var candidate) &&
            candidate.Scheme is "http" or "https")
        {
            return candidate;
        }

        return finalUrl;
    }

    // Reads the title, the description and the language, using Open Graph as the fallback.
    private static (string? Title, string? Description, string? Language) ReadMetadata(IDocument document)
    {
        string? Meta(string key)
        {
            foreach (var meta in document.QuerySelectorAll("meta"))
            {
                var name = meta.GetAttribute("name") ?? meta.GetAttribute("property");
                if (name is not null && name.Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    var content = TextTools.Collapse(meta.GetAttribute("content"));
                    if (content.Length > 0)
                    {
                        return content;
                    }
                }
            }

            return null;
        }

        var title = TextTools.Collapse(document.QuerySelector("head title, title")?.TextContent);
        if (title.Length == 0)
        {
            title = Meta("og:title") ?? string.Empty;
        }

        var description = Meta("description") ?? Meta("og:description") ?? Meta("twitter:description");

        var language = document.DocumentElement?.GetAttribute("lang")?.Trim();
        if (string.IsNullOrEmpty(language))
        {
            language = Meta("og:locale")?.Replace('_', '-');
        }

        return (
            title.Length == 0 ? null : TextTools.TruncateAtWord(title, 300),
            description is null ? null : TextTools.TruncateAtWord(description, 600),
            string.IsNullOrEmpty(language) || language.Length > 35 ? null : language);
    }

    // Reads the JSON-LD blocks: parsed, emptied of junk and written back compactly. Blocks that are
    // not valid JSON, or still too big after compacting, are dropped.
    private static IReadOnlyList<string> ReadJsonLd(IDocument document)
    {
        var result = new List<string>();
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            if (result.Count >= MaxJsonLdBlocks)
            {
                break;
            }

            var raw = script.TextContent;
            if (string.IsNullOrWhiteSpace(raw) || raw.Length > 500_000)
            {
                continue;
            }

            try
            {
                var node = JsonNode.Parse(raw, null, new JsonDocumentOptions
                {
                    MaxDepth = 32,
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });

                node = Prune(node, 0);
                if (node is null)
                {
                    continue;
                }

                var compact = node.ToJsonString();
                if (compact.Length <= MaxJsonLdChars && !result.Contains(compact))
                {
                    result.Add(compact);
                }
            }
            catch (JsonException)
            {
                // Invalid JSON-LD is common in the wild; skip it.
            }
        }

        return result;
    }

    // Removes the bulky or empty parts of a JSON-LD value: @context, potentialAction, empty values,
    // and strings over 300 characters are cut.
    private static JsonNode? Prune(JsonNode? node, int depth)
    {
        if (depth > 24)
        {
            return null;
        }

        switch (node)
        {
            case JsonObject obj:
            {
                var clean = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    if (key is "@context" or "potentialAction")
                    {
                        continue;
                    }

                    var pruned = Prune(value, depth + 1);
                    if (pruned is not null)
                    {
                        clean[key] = pruned;
                    }
                }

                return clean.Count == 0 ? null : clean;
            }

            case JsonArray array:
            {
                var clean = new JsonArray();
                foreach (var item in array)
                {
                    var pruned = Prune(item, depth + 1);
                    if (pruned is not null)
                    {
                        clean.Add(pruned);
                    }
                }

                return clean.Count == 0 ? null : clean;
            }

            case JsonValue value:
            {
                if (value.TryGetValue<string>(out var text))
                {
                    text = TextTools.Collapse(text);
                    return text.Length == 0 ? null : JsonValue.Create(TextTools.TruncateAtWord(text, 300));
                }

                return JsonNode.Parse(value.ToJsonString());
            }

            default:
                return null;
        }
    }

    // Script or stylesheet addresses written in the markup, made absolute (http and https only).
    private static IEnumerable<string> DomUrls(IDocument document, string selector, string attribute, Uri baseUri)
    {
        foreach (var element in document.QuerySelectorAll(selector))
        {
            var value = element.GetAttribute(attribute);
            if (!string.IsNullOrWhiteSpace(value) && TryAbsolute(baseUri, value, out var absolute))
            {
                yield return absolute.GetLeftPart(UriPartial.Path) + absolute.Query;
            }
        }
    }

    // Joins two address lists, dropping duplicates and over-long entries, capped in size.
    private static IReadOnlyList<string> MergeUrls(IEnumerable<string> first, IEnumerable<string> second)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var url in first.Concat(second))
        {
            if (result.Count >= MaxListedUrls)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(url) && url.Length <= MaxUrlChars && seen.Add(url))
            {
                result.Add(url);
            }
        }

        return result;
    }

    // ---- Noise removal -----------------------------------------------------------------------------

    // Deletes everything that is not visible page content.
    private static void RemoveNoise(IDocument document)
    {
        foreach (var element in document.QuerySelectorAll(NoiseSelector).ToList())
        {
            element.Remove();
        }

        // Hidden on purpose: the hidden attribute, aria-hidden, or an inline style that hides it.
        foreach (var element in document.QuerySelectorAll("[hidden],[aria-hidden='true'],[style]").ToList())
        {
            if (element.LocalName is "html" or "body")
            {
                continue;
            }

            var hidden = element.HasAttribute("hidden") ||
                         string.Equals(element.GetAttribute("aria-hidden"), "true", StringComparison.OrdinalIgnoreCase);
            if (!hidden)
            {
                var style = element.GetAttribute("style");
                hidden = style is not null && SafeIsMatch(HiddenStyle(), style);
            }

            if (hidden)
            {
                element.Remove();
            }
        }

        // Cookie and consent banners: small containers named after them. A big container with such
        // a word in its class is real content (a page about cookies) and is kept.
        foreach (var element in document.QuerySelectorAll("[id],[class]").ToList())
        {
            if (element.LocalName is "html" or "body" or "main" or "article" || element.Parent is null)
            {
                continue;
            }

            var label = (element.GetAttribute("id") ?? string.Empty) + " " + (element.GetAttribute("class") ?? string.Empty);
            if (label.Length > 400 || !SafeIsMatch(BannerWords(), label))
            {
                continue;
            }

            if (element.TextContent.Length <= 2000 && element.QuerySelector("main,article,h1") is null)
            {
                element.Remove();
            }
        }
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

    // Prefers <main> (or role=main), then a lone <article>, when it holds real text; else the body.
    private static IElement ChooseRoot(IDocument document, IElement body)
    {
        var main = document.QuerySelector("main,[role='main']");
        if (main is not null && main.TextContent.Length >= MinMainChars)
        {
            return main;
        }

        var articles = document.QuerySelectorAll("article");
        if (articles.Length == 1 && articles[0].TextContent.Length >= MinMainChars)
        {
            return articles[0];
        }

        return body;
    }

    // ---- Text blocks -------------------------------------------------------------------------------

    // One line of body text with its priority (0 = real content, 1 = short fragment, 2 = page furniture
    // such as navigation or footer) and its position in the document.
    private sealed record Block(int Index, string Text, int Tier);

    // Walks the content tree and collects text blocks in document order.
    private sealed class Walker
    {
        private readonly StringBuilder _line = new();
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
        private bool _furniture;
        private int _contentDepth;

        // Creates a walker; startInContent is true when the walk begins inside <main> or <article>.
        public Walker(bool startInContent)
        {
            _contentDepth = startInContent ? 1 : 0;
        }

        // The blocks found so far, in document order.
        public List<Block> Blocks { get; } = [];

        // Walks every child of the node.
        public void WalkChildren(INode parent, int depth)
        {
            foreach (var child in parent.ChildNodes)
            {
                Walk(child, depth);
            }

            if (depth == 0)
            {
                Flush();
            }
        }

        // Walks one node: text is added to the current line; block elements end the line.
        private void Walk(INode node, int depth)
        {
            if (node is IText text)
            {
                _line.Append(text.Data).Append(' ');
                return;
            }

            if (node is not IElement element)
            {
                return;
            }

            var name = element.LocalName;
            if (HeadingTags.Contains(name))
            {
                Flush();
                return;
            }

            if (name == "br")
            {
                Flush();
                return;
            }

            if (depth >= MaxDepth)
            {
                // Too deep to walk safely: take what the subtree says as one block and stop.
                Flush();
                _line.Append(FlatText(element));
                Flush();
                return;
            }

            if (name == "tr")
            {
                Flush();
                AddRow(element);
                return;
            }

            var isBlock = BlockTags.Contains(name);
            var enters = name is "main" or "article";
            var furniture = IsFurniture(element);

            if (isBlock)
            {
                Flush();
            }

            var previousFurniture = _furniture;
            _furniture |= furniture;
            if (enters)
            {
                _contentDepth++;
            }

            WalkChildren(element, depth + 1);

            if (isBlock)
            {
                Flush();
            }

            if (enters)
            {
                _contentDepth--;
            }

            _furniture = previousFurniture;
        }

        // True for navigation, footer, aside, and a site-level header (a header inside <main> or
        // <article> belongs to that content).
        private bool IsFurniture(IElement element)
        {
            var name = element.LocalName;
            if (name is "nav" or "footer" or "aside")
            {
                return true;
            }

            if (name == "header")
            {
                return _contentDepth == 0;
            }

            var role = element.GetAttribute("role");
            return role is not null && FurnitureRoles.Contains(role.Trim());
        }

        // A table row becomes one line: its cells joined by " | ".
        private void AddRow(IElement row)
        {
            var cells = new List<string>();
            foreach (var cell in row.Children)
            {
                if (cell.LocalName is "td" or "th")
                {
                    var cellText = TextTools.Collapse(FlatText(cell));
                    if (cellText.Length > 0)
                    {
                        cells.Add(cellText);
                    }
                }
            }

            if (cells.Count > 0)
            {
                _line.Append(string.Join(" | ", cells));
                Flush();
            }
        }

        // All the visible text under an element, flattened (no structure).
        private static string FlatText(INode node)
        {
            var sb = new StringBuilder();
            var stack = new Stack<INode>();
            stack.Push(node);
            while (stack.Count > 0 && sb.Length < MaxBlockChars * 2)
            {
                var current = stack.Pop();
                if (current is IText t)
                {
                    sb.Append(t.Data).Append(' ');
                }
                else if (current is IElement e && !HeadingTags.Contains(e.LocalName) || ReferenceEquals(current, node))
                {
                    var children = current.ChildNodes;
                    for (var i = children.Length - 1; i >= 0; i--)
                    {
                        stack.Push(children[i]);
                    }
                }
            }

            return sb.ToString();
        }

        // Ends the current line: cleans it and, if it is worth keeping, adds it as a block.
        private void Flush()
        {
            if (_line.Length == 0)
            {
                return;
            }

            var raw = _line.ToString();
            _line.Clear();

            var line = CleanLine(raw);
            if (line is null || !_seen.Add(line))
            {
                return;
            }

            var tier = _furniture ? 2 : line.Length >= 40 ? 0 : 1;
            Blocks.Add(new Block(Blocks.Count, line, tier));
        }
    }

    // Cleans one line of text. Returns null when the line should be dropped: too short, no letters
    // or digits, or a cookie banner phrase.
    private static string? CleanLine(string raw)
    {
        var line = TextTools.Collapse(raw);
        if (line.Length < 3)
        {
            return null;
        }

        if (line.Contains("data:", StringComparison.OrdinalIgnoreCase))
        {
            line = TextTools.Collapse(SafeReplace(DataUri(), line));
        }

        // Drop single "words" that are really URLs, tokens or base64.
        if (line.Length > MaxWordChars)
        {
            var words = line.Split(' ');
            if (words.Any(w => w.Length > MaxWordChars))
            {
                line = string.Join(' ', words.Where(w => w.Length <= MaxWordChars));
            }
        }

        if (line.Length < 3 || !line.Any(char.IsLetterOrDigit))
        {
            return null;
        }

        if (line.Length <= 400)
        {
            foreach (var phrase in CookiePhrases)
            {
                if (line.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }
        }

        return line.Length > MaxBlockChars ? TextTools.TruncateAtWord(line, MaxBlockChars) : line;
    }

    // Regex replace that treats a timeout as "leave the text alone".
    private static string SafeReplace(Regex regex, string input)
    {
        try
        {
            return regex.Replace(input, string.Empty);
        }
        catch (RegexMatchTimeoutException)
        {
            return input;
        }
    }

    // Picks blocks by priority until the budget is used, then returns them in document order. Whole
    // blocks only, except that one block may be cut at a word boundary to use the remaining room.
    private static string SelectText(IReadOnlyList<Block> blocks, int maxChars)
    {
        var chosen = new Dictionary<int, string>();
        var used = 0;
        var cutDone = false;

        for (var tier = 0; tier <= 2 && used < maxChars; tier++)
        {
            foreach (var block in blocks)
            {
                if (block.Tier != tier)
                {
                    continue;
                }

                var separator = chosen.Count > 0 ? 1 : 0;
                if (used + separator + block.Text.Length <= maxChars)
                {
                    chosen[block.Index] = block.Text;
                    used += separator + block.Text.Length;
                    continue;
                }

                // Use up the room with a word-boundary cut of the first block that does not fit.
                var room = maxChars - used - separator;
                if (!cutDone && room >= 80)
                {
                    var cut = TextTools.TruncateAtWord(block.Text, room);
                    if (cut.Length > 0)
                    {
                        chosen[block.Index] = cut;
                        used += separator + cut.Length;
                        cutDone = true;
                    }
                }

                if (maxChars - used < 40)
                {
                    break;
                }
            }
        }

        return string.Join("\n", chosen.OrderBy(pair => pair.Key).Select(pair => pair.Value));
    }

    // ---- Outline and links -------------------------------------------------------------------------

    // The h1 to h4 headings in document order, skipping those inside navigation and footers.
    private static IReadOnlyList<HeadingNode> BuildOutline(IElement body, int maxHeadings)
    {
        var outline = new List<HeadingNode>();
        foreach (var heading in body.QuerySelectorAll("h1,h2,h3,h4"))
        {
            if (outline.Count >= maxHeadings)
            {
                break;
            }

            if (HasAncestor(heading, "nav", "footer", "aside"))
            {
                continue;
            }

            var text = TextTools.Collapse(heading.TextContent);
            if (text.Length < 2)
            {
                continue;
            }

            var node = new HeadingNode(heading.LocalName[1] - '0', TextTools.TruncateAtWord(text, 160));
            if (outline.Count == 0 || outline[^1] != node)
            {
                outline.Add(node);
            }
        }

        return outline;
    }

    // True when one of the element's ancestors has one of the given tag names.
    private static bool HasAncestor(IElement element, params string[] names)
    {
        var parent = element.ParentElement;
        for (var steps = 0; parent is not null && steps < 400; steps++, parent = parent.ParentElement)
        {
            if (names.Contains(parent.LocalName))
            {
                return true;
            }
        }

        return false;
    }

    // The page's links: absolute, http(s) only, one per address, with text, internal ones from the
    // navigation and main area first, capped.
    private static IReadOnlyList<LinkRef> BuildLinks(IElement body, Uri baseUri, Uri finalUrl, int maxLinks)
    {
        if (maxLinks <= 0)
        {
            return [];
        }

        var pageHost = HostKey(finalUrl);
        var pageUrl = finalUrl.GetLeftPart(UriPartial.Path) + finalUrl.Query;
        var found = new Dictionary<string, (int Priority, int Order, string Text)>(StringComparer.Ordinal);

        foreach (var anchor in body.QuerySelectorAll("a[href]"))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) || !TryAbsolute(baseUri, href, out var absolute))
            {
                continue;
            }

            var address = absolute.GetLeftPart(UriPartial.Path) + absolute.Query;
            if (address.Length > MaxUrlChars || address == pageUrl || found.ContainsKey(address))
            {
                continue;
            }

            var text = LinkText(anchor);
            if (text.Length == 0)
            {
                continue;
            }

            var internalLink = HostKey(absolute) == pageHost;
            var prominent = !HasAncestor(anchor, "footer", "aside");
            var priority = internalLink ? (prominent ? 0 : 1) : 2;
            found[address] = (priority, found.Count, TextTools.TruncateAtWord(text, 120));
        }

        return found
            .OrderBy(pair => pair.Value.Priority)
            .ThenBy(pair => pair.Value.Order)
            .Take(maxLinks)
            .Select(pair => new LinkRef(pair.Value.Text, pair.Key))
            .ToList();
    }

    // The words that describe a link: its text, else an image's alt text inside it, else its title.
    private static string LinkText(IElement anchor)
    {
        var text = TextTools.Collapse(anchor.TextContent);
        if (text.Length > 0)
        {
            return text;
        }

        var alt = TextTools.Collapse(anchor.QuerySelector("img[alt]")?.GetAttribute("alt"));
        return alt.Length > 0 ? alt : TextTools.Collapse(anchor.GetAttribute("title"));
    }

    // Host name without "www." and in lower case, for telling internal links from external ones.
    private static string HostKey(Uri uri)
    {
        var host = uri.IdnHost.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    // Resolves an href against the base address. Only http and https results are returned, with
    // no #fragment; javascript:, mailto:, tel:, data: and the like are rejected.
    private static bool TryAbsolute(Uri baseUri, string href, out Uri absolute)
    {
        absolute = baseUri;
        var trimmed = href.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 2048 || trimmed.StartsWith('#'))
        {
            return false;
        }

        if (!Uri.TryCreate(baseUri, trimmed, out var resolved) ||
            resolved.Scheme is not ("http" or "https") ||
            resolved.UserInfo.Length > 0)
        {
            return false;
        }

        absolute = resolved;
        return true;
    }
}
