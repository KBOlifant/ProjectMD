using Microsoft.Extensions.Options;

namespace ProjectMD.Api.Services.Extraction;

// The free half of a scan: fetch the page with the browser, then reduce it to a PageDigest. No AI
// and no credits are involved. Each step is a separate service so each can be replaced in tests.
public sealed class ExtractionService : IExtractionService
{
    private readonly IPageFetcher _fetcher;
    private readonly IHtmlReducer _reducer;
    private readonly ExtractionOptions _options;

    // Creates the service from its two steps and the settings.
    public ExtractionService(IPageFetcher fetcher, IHtmlReducer reducer, IOptions<ExtractionOptions> options)
    {
        _fetcher = fetcher;
        _reducer = reducer;
        _options = options.Value;
    }

    // Fetches and reduces the page. A site that answers with an HTTP error (404, 500...) is an
    // error here, because documenting an error page would be wrong and would waste the user's scan
    // (turn off with Scanner:RejectHttpErrors=false).
    public async Task<PageDigest> ExtractAsync(Uri url, CancellationToken ct)
    {
        var raw = await _fetcher.FetchAsync(url, ct);

        if (_options.RejectHttpErrors && raw.StatusCode >= 400)
        {
            throw new ExtractionException(
                ExtractionFailure.HttpError,
                $"The site responded with an error (HTTP {raw.StatusCode}), so there was nothing to document.");
        }

        return _reducer.Reduce(raw, new ReduceOptions(_options.MaxTextChars, _options.MaxLinks, _options.MaxHeadings));
    }
}
