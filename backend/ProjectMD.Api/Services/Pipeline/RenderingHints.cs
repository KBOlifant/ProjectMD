using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Services.Pipeline;

// Short, factual hints about how the page is rendered, worked out from markers the extractor saw
// and from how much text the HTML held. They are guesses from signals (labelled as such), shared by
// the prompt and the document so both say the same thing.
public static class RenderingHints
{
    // Less readable text than this, together with scripts, suggests the page is drawn in the browser.
    private const int ThinTextChars = 300;

    // The hints, in a fixed order. Empty when nothing can be said.
    public static IReadOnlyList<string> Describe(PageDigest digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        var markers = new HashSet<string>(digest.Markers ?? [], StringComparer.OrdinalIgnoreCase);
        var hints = new List<string>();

        if (markers.Contains("js:__NEXT_DATA__"))
        {
            hints.Add("Next.js page data is embedded (server-rendered or statically generated React)");
        }

        if (markers.Contains("js:__NUXT__"))
        {
            hints.Add("Nuxt payload is embedded (server-rendered Vue)");
        }

        if (markers.Contains("attr:data-astro-cid"))
        {
            hints.Add("Astro markup (mostly static HTML)");
        }

        if (digest.Text.Length >= ThinTextChars)
        {
            hints.Add("readable content is present in the rendered HTML");
        }
        else if (digest.ScriptUrls.Count > 0)
        {
            hints.Add("little readable text in the HTML, so the page is probably drawn in the browser by scripts");
        }

        return hints;
    }
}
