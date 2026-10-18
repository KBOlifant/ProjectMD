using Microsoft.Playwright;

namespace ProjectMD.Api.Services.Extraction.Components;

// Reads the look of a few main components from a page that is already loaded (3 second cap). Any
// failure gives null: a scan never fails because of this. Details are only logged at Debug level.
public static class ComponentCapture
{
    public static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(3);

    public static async Task<ComponentStyles?> TryCaptureAsync(IPage page, ILogger logger, CancellationToken ct)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(ScriptTimeout);
            var json = await page.EvaluateAsync<string>(ComponentScript.Source).WaitAsync(limit.Token);
            return ComponentSample.TryParse(json);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Component capture skipped");
            return null;
        }
    }
}
