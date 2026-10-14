using System.Text.RegularExpressions;
using AngleSharp.Dom;

namespace ProjectMD.Api.Services.Extraction;

// Reads the page markup BEFORE the reducer strips it and returns short marker tokens that the tech
// fingerprinter can match (the reducer throws the markup away, so this is the only chance). The
// vocabulary is fixed here and referenced by name in TechRules.json:
//   generator:<text>   the <meta name="generator"> value
//   og:site_name=<x> / og:type=<x>   Open Graph hints
//   id:<x>             an element with a framework root id, such as id:__next
//   attr:<x>           an attribute (or attribute prefix) seen anywhere, such as attr:ng-version
//   class:<x>          a class family seen, such as class:svelte- or the heuristics class:tailwind
//   js:<x>             a telltale string inside an inline script, such as js:__NEXT_DATA__
// Nothing here runs scripts or touches the network.
public static partial class MarkerExtractor
{
    // The most markers returned for one page.
    private const int MaxMarkers = 80;

    // The most elements examined (a safety limit for huge pages).
    private const int MaxElements = 30000;

    // The most inline scripts examined, and how much of each.
    private const int MaxInlineScripts = 60;

    private const int MaxInlineScriptChars = 30000;

    // Ids that frameworks put on their root element.
    private static readonly string[] RootIds =
        ["__next", "__nuxt", "___gatsby", "__gatsby", "root", "app", "__svelte", "svelte", "q-app"];

    // Attribute names (exact) or prefixes (ending in "-" or "_") that identify a framework.
    private static readonly (string Name, bool Prefix)[] AttributeMarkers =
    [
        ("ng-version", false),
        ("_ngcontent", true),
        ("_nghost", true),
        ("data-reactroot", false),
        ("data-reactid", false),
        ("data-v-", true),
        ("data-server-rendered", false),
        ("data-astro-cid", true),
        ("data-wf-", true),
        ("data-sveltekit", true),
        ("data-framer", true),
    ];

    // Substrings that, inside an inline script, identify a library. Matched case-sensitively.
    private static readonly string[] InlineScriptTokens =
    [
        "__NEXT_DATA__", "__next_f", "__NUXT__", "Shopify.", "dataLayer", "gtag(", "GoogleAnalyticsObject",
        "fbq(", "_hsq", "posthog", "Intercom", "Sentry.", "firebase", "analytics.load", "_hjSettings",
        "Webflow", "wixBiSession", "SQUARESPACE_CONTEXT", "__sveltekit", "__remixContext", "algoliasearch",
        "Stripe(",
    ];

    // Tailwind-only class shapes: responsive/state variants (md:flex), colour scales (bg-gray-100),
    // text sizes (text-sm) and font weights (font-bold). Spacing is left out on purpose because
    // Bootstrap uses the same p-3 / mt-2 names.
    [GeneratedRegex(@"^(?:(?:sm|md|lg|xl|2xl|hover|focus|dark|group-hover|disabled):[a-z\[][\w\-\[\]/.%:]*|(?:text|bg|border|ring|from|to|via|fill|stroke)-[a-z]+-(?:50|100|200|300|400|500|600|700|800|900|950)|text-(?:xs|sm|base|lg|xl|[2-9]xl)|font-(?:thin|light|normal|medium|semibold|bold|extrabold|black))$",
        RegexOptions.CultureInvariant, 250)]
    private static partial Regex TailwindToken();

    // Bootstrap-only class shapes: grid columns, buttons, navbar, cards, forms.
    [GeneratedRegex(@"^(?:col(?:-(?:sm|md|lg|xl|xxl))?-(?:[1-9]|1[0-2])|btn-(?:primary|secondary|success|danger|warning|info|light|dark|outline-[a-z]+)|navbar(?:-expand(?:-(?:sm|md|lg|xl))?|-toggler|-brand|-nav)|d-(?:flex|none|block)|justify-content-[a-z]+|align-items-[a-z]+|card-body|card-title|modal-dialog|form-control|form-group|input-group|list-group-item|dropdown-menu|nav-link|nav-item|carousel-item|alert-(?:primary|success|danger|warning|info))$",
        RegexOptions.CultureInvariant, 250)]
    private static partial Regex BootstrapToken();

    // How many different Tailwind or Bootstrap class names must appear before we report the family.
    private const int TailwindThreshold = 6;

    private const int BootstrapThreshold = 5;

    // Collects the markers for a parsed document.
    public static IReadOnlyList<string> Extract(IDocument document)
    {
        var markers = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var meta in document.QuerySelectorAll("meta"))
        {
            var key = (meta.GetAttribute("name") ?? meta.GetAttribute("property") ?? string.Empty).Trim().ToLowerInvariant();
            var content = TextTools.Collapse(meta.GetAttribute("content"));
            if (content.Length == 0)
            {
                continue;
            }

            switch (key)
            {
                case "generator":
                    markers.Add("generator:" + Clip(content, 80));
                    break;
                case "og:site_name":
                    markers.Add("og:site_name=" + Clip(content, 80));
                    break;
                case "og:type":
                    markers.Add("og:type=" + Clip(content, 40));
                    break;
            }
        }

        foreach (var id in RootIds)
        {
            if (document.GetElementById(id) is not null)
            {
                markers.Add("id:" + id);
            }
        }

        var tailwind = new HashSet<string>(StringComparer.Ordinal);
        var bootstrap = new HashSet<string>(StringComparer.Ordinal);
        var inlineScripts = 0;
        var seen = 0;

        foreach (var element in document.QuerySelectorAll("*"))
        {
            if (++seen > MaxElements)
            {
                break;
            }

            if (element.LocalName.Equals("astro-island", StringComparison.Ordinal))
            {
                markers.Add("attr:data-astro-cid");
            }

            foreach (var attribute in element.Attributes)
            {
                var name = attribute.Name;
                foreach (var (marker, prefix) in AttributeMarkers)
                {
                    if (prefix ? name.StartsWith(marker, StringComparison.Ordinal) : name.Equals(marker, StringComparison.Ordinal))
                    {
                        markers.Add("attr:" + marker);
                    }
                }

                if (name.Equals("class", StringComparison.Ordinal))
                {
                    ClassifyClasses(attribute.Value, markers, tailwind, bootstrap);
                }
            }

            if (element.LocalName.Equals("script", StringComparison.Ordinal))
            {
                ScanScript(element, markers, ref inlineScripts);
            }
        }

        if (tailwind.Count >= TailwindThreshold)
        {
            markers.Add("class:tailwind");
        }

        if (bootstrap.Count >= BootstrapThreshold)
        {
            markers.Add("class:bootstrap");
        }

        return markers.Take(MaxMarkers).ToList();
    }

    // Looks at the class names of one element: framework prefixes are added straight away; the
    // Tailwind and Bootstrap shapes are counted into the two sets.
    private static void ClassifyClasses(string classValue, SortedSet<string> markers, HashSet<string> tailwind, HashSet<string> bootstrap)
    {
        foreach (var token in classValue.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length > 80)
            {
                continue;
            }

            if (token.StartsWith("svelte-", StringComparison.Ordinal) && token.Length > 8)
            {
                markers.Add("class:svelte-");
            }
            else if (token.StartsWith("Mui", StringComparison.Ordinal) && token.Length > 4)
            {
                markers.Add("class:Mui");
            }
            else if (token.StartsWith("wp-block-", StringComparison.Ordinal))
            {
                markers.Add("class:wp-block-");
            }

            try
            {
                if (tailwind.Count < TailwindThreshold && TailwindToken().IsMatch(token))
                {
                    tailwind.Add(token);
                }

                if (bootstrap.Count < BootstrapThreshold && BootstrapToken().IsMatch(token))
                {
                    bootstrap.Add(token);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // A pathological class name: ignore it.
            }
        }
    }

    // Looks at one script element: the ids that frameworks use for their data blobs, and the
    // telltale strings in inline code (external scripts are handled elsewhere, by address).
    private static void ScanScript(IElement script, SortedSet<string> markers, ref int inlineScripts)
    {
        var id = script.GetAttribute("id");
        if (id is "__NEXT_DATA__")
        {
            markers.Add("js:__NEXT_DATA__");
        }
        else if (id is "__NUXT_DATA__")
        {
            markers.Add("js:__NUXT__");
        }

        if (!string.IsNullOrEmpty(script.GetAttribute("src")))
        {
            return;
        }

        var type = (script.GetAttribute("type") ?? string.Empty).Trim().ToLowerInvariant();
        if (type.Length > 0 && type is not ("text/javascript" or "application/javascript" or "module" or "text/babel"))
        {
            return;
        }

        if (inlineScripts++ >= MaxInlineScripts)
        {
            return;
        }

        var code = script.TextContent;
        if (code.Length > MaxInlineScriptChars)
        {
            code = code[..MaxInlineScriptChars];
        }

        foreach (var token in InlineScriptTokens)
        {
            if (code.Contains(token, StringComparison.Ordinal))
            {
                markers.Add("js:" + token);
            }
        }
    }

    // Shortens a value for use inside a marker.
    private static string Clip(string value, int max) => value.Length <= max ? value : value[..max];
}
