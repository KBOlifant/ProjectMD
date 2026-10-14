namespace ProjectMD.Api.Services.Extraction;

// Settings for fetching and reducing pages. Bound from the "Scanner" configuration section (the
// same section also holds Scanner:Mode, which belongs to the pipeline). Every value has a safe
// default, so nothing needs to be configured.
public sealed class ExtractionOptions
{
    // The configuration section these settings are read from.
    public const string SectionName = "Scanner";

    // How many pages may be rendered at the same time (Scanner:MaxConcurrentFetches).
    public int MaxConcurrentFetches { get; set; } = 2;

    // The most HTML we keep per page, in UTF-8 bytes; more is cut off, not rejected (Scanner:MaxHtmlBytes).
    public int MaxHtmlBytes { get; set; } = 2 * 1024 * 1024;

    // How long one navigation may take before we give up waiting for the page to settle.
    public int NavigationTimeoutSeconds { get; set; } = 30;

    // Hard limit for the whole fetch, whatever the page does.
    public int OverallTimeoutSeconds { get; set; } = 45;

    // The largest script, stylesheet or data file one page may load; a bigger one is refused.
    public int MaxResourceBytes { get; set; } = 5 * 1024 * 1024;

    // The most bytes one page may load in total (HTML, scripts, styles and data); later requests are refused.
    public int MaxTotalBytes { get; set; } = 25 * 1024 * 1024;

    // How long one network request (headers and body) may take.
    public int RequestTimeoutSeconds { get; set; } = 15;

    // How long a request may wait for a free render slot before it is refused as busy.
    public int QueueWaitSeconds { get; set; } = 30;

    // The most redirects one request may follow.
    public int MaxRedirects { get; set; } = 8;

    // The most network requests one page may make before further ones are blocked.
    public int MaxRequestsPerPage { get; set; } = 300;

    // Optional: use an installed browser instead of the downloaded Chromium ("chrome" or "msedge").
    // Empty (the default) uses Playwright's own Chromium.
    public string BrowserChannel { get; set; } = string.Empty;

    // When true, a page answered with HTTP 400 or above is an error, not a page to document.
    public bool RejectHttpErrors { get; set; } = true;

    // Reducer limits (see ReduceOptions).
    public int MaxTextChars { get; set; } = 12000;

    public int MaxLinks { get; set; } = 40;

    public int MaxHeadings { get; set; } = 60;

    // True when every number is in a sensible range. Used by the options validation.
    public static bool IsValid(ExtractionOptions o) =>
        o.MaxConcurrentFetches is >= 1 and <= 16 &&
        o.MaxHtmlBytes is >= 10_000 and <= 50_000_000 &&
        o.MaxResourceBytes is >= 10_000 and <= 100_000_000 &&
        o.MaxTotalBytes is >= 10_000 and <= 500_000_000 &&
        o.RequestTimeoutSeconds is >= 1 and <= 120 &&
        o.NavigationTimeoutSeconds is >= 1 and <= 120 &&
        o.OverallTimeoutSeconds is >= 1 and <= 300 &&
        o.QueueWaitSeconds is >= 1 and <= 300 &&
        o.MaxRedirects is >= 0 and <= 30 &&
        o.MaxRequestsPerPage is >= 1 and <= 5000 &&
        o.MaxTextChars is >= 500 and <= 200_000 &&
        o.MaxLinks is >= 0 and <= 500 &&
        o.MaxHeadings is >= 0 and <= 500;
}
