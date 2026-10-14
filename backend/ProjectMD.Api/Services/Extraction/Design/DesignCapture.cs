using Microsoft.Playwright;

namespace ProjectMD.Api.Services.Extraction.Design;

// Captures the design-system summary of a page that is already loaded in the browser. It runs the
// style-sampling script (5 second cap), then combines the result with the stylesheet text the
// request proxy already fetched. The stylesheet text is parsed and dropped; only the small
// DesignSystem summary is kept. Any failure at all gives null: a scan never fails because of design
// capture. Details of what was found are never logged above Debug level.
public static class DesignCapture
{
    // How long the in-page script may run.
    public static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(5);

    // The longest script answer that is parsed (it should be a few KB).
    private const int MaxSampleChars = 200_000;

    // Samples the page and builds the summary; null when anything goes wrong. Only the caller's
    // own cancellation is rethrown.
    public static async Task<DesignSystem?> TryCaptureAsync(
        IPage page,
        IReadOnlyList<string> stylesheets,
        string? renderedHtml,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(ScriptTimeout);

            var json = await page.EvaluateAsync<string>(DesignScript.Source).WaitAsync(limit.Token);
            if (json is null || json.Length > MaxSampleChars || !DesignSample.TryParse(json, out var sample))
            {
                return null;
            }

            var sheets = new List<string>(stylesheets.Count + CssAnalyzer.MaxSheets);
            sheets.AddRange(stylesheets);
            sheets.AddRange(CssAnalyzer.InlineStyles(renderedHtml));
            return DesignAnalyzer.Build(sample, sheets);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Design capture skipped");
            return null;
        }
    }
}
