namespace ProjectMD.Api.Services.Extraction;

// One heading in the page outline: its level (1 to 4) and its text.
public sealed record HeadingNode(int Level, string Text);

// One link on the page: the visible text and the absolute address it points to.
public sealed record LinkRef(string Text, string Href);

// The small, clean summary of a page that the rest of the pipeline works with. It is built without
// any AI. Sizes: RawHtmlChars is the length of the HTML the browser returned; ReducedChars is the
// length of everything below that would be sent on (title, description, outline, text, links and
// JSON-LD); EstimatedTokens is ceil(ReducedChars / 4), the usual rule of thumb of about four
// characters per token (an approximation, not a real tokenizer count). ContentHash is a SHA-256 hex
// string of the normalised title, description, outline and text, stable across runs (see HtmlReducer).
// Markers is an additive extra (it has a default, so code written against the 16 contract members
// still compiles): short tokens such as "generator:WordPress 6.4", "id:__next" or "js:__NEXT_DATA__"
// that the reducer saw in the page markup and that the tech fingerprinter reads, because the
// markup itself is not kept.
public sealed record PageDigest(
    Uri FinalUrl,
    int StatusCode,
    string? Title,
    string? MetaDescription,
    string? Language,
    IReadOnlyList<HeadingNode> Outline,
    string Text,
    IReadOnlyList<LinkRef> Links,
    IReadOnlyList<string> ScriptUrls,
    IReadOnlyList<string> StylesheetUrls,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<string> JsonLd,
    int RawHtmlChars,
    int ReducedChars,
    int EstimatedTokens,
    string ContentHash,
    IReadOnlyList<string>? Markers = null);

// Size limits for the reduced page. The text is cut by priority, never in the middle of a word.
public sealed record ReduceOptions(int MaxTextChars = 12000, int MaxLinks = 40, int MaxHeadings = 60);

// Turns raw rendered HTML into a PageDigest. Pure: no network, no AI.
public interface IHtmlReducer
{
    // Reduces the page. Never throws for odd HTML.
    PageDigest Reduce(RawPage page, ReduceOptions options);
}
