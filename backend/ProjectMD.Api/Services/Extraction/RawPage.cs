namespace ProjectMD.Api.Services.Extraction;

// What the browser saw for one page: the rendered HTML (after JavaScript ran, cut to the size
// limit), the status and a few useful response headers, and the addresses of the scripts and
// stylesheets the page loaded (the addresses only, never their contents).
public sealed record RawPage(
    Uri FinalUrl,
    int StatusCode,
    string Html,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<string> ScriptUrls,
    IReadOnlyList<string> StylesheetUrls);

// Loads a public web page and returns what the browser rendered. The only implementation is
// PlaywrightPageFetcher. Every request it makes is checked against the SSRF rules first.
public interface IPageFetcher
{
    // Fetches the page. Throws ExtractionException (with a safe message) for any failure.
    Task<RawPage> FetchAsync(Uri url, CancellationToken ct);
}
