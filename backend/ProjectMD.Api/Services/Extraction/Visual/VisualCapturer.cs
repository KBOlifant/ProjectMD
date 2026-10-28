using Microsoft.Playwright;

namespace ProjectMD.Api.Services.Extraction.Visual;

// Takes the screenshots and reads the animation evidence of a page that is already loaded:
//  1. reads the evidence once (animations that run on load),
//  2. takes a screenshot of the first screen,
//  3. scrolls down Motion:ScrollStepPixels with real mouse-wheel events (smooth-scroll libraries
//     such as Lenis only react to those), waits Motion:FrameDelayMs, and takes the next screenshot,
//     Motion:ScrollFrames times in all,
//  4. reads the evidence again (scroll animations only exist once they are reached),
//  5. hovers a few elements to find hover states (HoverProbe),
//  6. lays the frames out on contact sheets in a separate, blank, script-free page.
// Everything has a time limit (Motion:CaptureTimeoutSeconds). Nothing here can fail a scan: on any
// trouble, whatever was captured so far is kept, and with nothing at all the answer is null.
public static class VisualCapturer
{
    // Limits for single steps.
    private static readonly TimeSpan EvidenceTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SheetTimeout = TimeSpan.FromSeconds(10);
    private const float ScreenshotTimeoutMs = 8000;

    // Captures the page; null when nothing at all could be captured. Only the caller's own
    // cancellation is rethrown.
    public static async Task<VisualCapture?> TryCaptureAsync(
        IPage page,
        MotionOptions options,
        IReadOnlyList<string> scriptUrls,
        IReadOnlyList<string> stylesheets,
        ILogger logger,
        CancellationToken ct)
    {
        var frames = new List<byte[]>();
        MotionEvidence? onLoad = null;
        MotionEvidence? afterScroll = null;

        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            limit.CancelAfter(TimeSpan.FromSeconds(options.CaptureTimeoutSeconds));
            var token = limit.Token;
            try
            {
                onLoad = await TryReadEvidenceAsync(page, scriptUrls, stylesheets, logger, token);

                // Put the mouse in the middle of the window so wheel events land on the page.
                await page.Mouse.MoveAsync(options.ViewportWidth / 2f, options.ViewportHeight / 2f).WaitAsync(token);
                frames.Add(await ShootAsync(page, options, token));

                for (var i = 1; i < options.ScrollFrames; i++)
                {
                    await page.Mouse.WheelAsync(0, options.ScrollStepPixels).WaitAsync(token);
                    if (options.FrameDelayMs > 0)
                    {
                        await Task.Delay(options.FrameDelayMs, token);
                    }

                    frames.Add(await ShootAsync(page, options, token));
                }

                afterScroll = await TryReadEvidenceAsync(page, scriptUrls, stylesheets, logger, token);

                // Last, because hovering scrolls the page: the screenshots are already taken.
                afterScroll = MotionEvidenceReader.WithHoverStates(afterScroll, await HoverProbe.TryProbeAsync(page, options, logger, token));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The time limit or a page problem: keep what we have.
                logger.LogDebug(ex, "Visual capture stopped early after {Frames} frames", frames.Count);
            }
        }

        var motion = MotionEvidenceReader.Merge(onLoad, afterScroll);
        if (frames.Count == 0 && motion is null)
        {
            return null;
        }

        IReadOnlyList<byte[]> sheets = frames.Count >= 2
            ? await TryBuildSheetsAsync(page, frames, options, logger, ct)
            : Array.Empty<byte[]>();

        return new VisualCapture(
            options.ViewportWidth,
            options.ViewportHeight,
            options.ScrollStepPixels,
            options.FrameDelayMs,
            frames,
            sheets,
            motion);
    }

    // One JPEG screenshot of what is in the window right now.
    private static Task<byte[]> ShootAsync(IPage page, MotionOptions options, CancellationToken token) =>
        page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Jpeg,
            Quality = options.ScreenshotQuality,
            FullPage = false,
            Timeout = ScreenshotTimeoutMs,
        }).WaitAsync(token);

    // Runs MotionScript in the page and reads its answer; null when it failed or timed out.
    private static async Task<MotionEvidence?> TryReadEvidenceAsync(
        IPage page,
        IReadOnlyList<string> scriptUrls,
        IReadOnlyList<string> stylesheets,
        ILogger logger,
        CancellationToken token)
    {
        string? json = null;
        try
        {
            json = await page.EvaluateAsync<string>(MotionScript.Source).WaitAsync(EvidenceTimeout, token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Motion evidence script failed");
        }

        // The script addresses and CSS still give evidence when the script failed.
        return MotionEvidenceReader.Read(json, scriptUrls, stylesheets);
    }

    // Renders the contact sheets in a fresh browser context with JavaScript off. The page content is
    // our own HTML with our own JPEGs as data: URLs, so it loads nothing; the browser cannot resolve
    // host names anyway (see PlaywrightPageFetcher). Empty list on any failure.
    private static async Task<IReadOnlyList<byte[]>> TryBuildSheetsAsync(
        IPage page,
        IReadOnlyList<byte[]> frames,
        MotionOptions options,
        ILogger logger,
        CancellationToken ct)
    {
        var browser = page.Context.Browser;
        if (browser is null)
        {
            return [];
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(SheetTimeout);
        var token = limit.Token;

        IBrowserContext? context = null;
        var sheets = new List<byte[]>();
        try
        {
            context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                JavaScriptEnabled = false,
                AcceptDownloads = false,
                ServiceWorkers = ServiceWorkerPolicy.Block,
                ViewportSize = new ViewportSize
                {
                    Width = ContactSheet.ThumbWidth(options) * Math.Max(1, options.SheetColumns),
                    Height = ContactSheet.ThumbHeight(options),
                },
            }).WaitAsync(token);

            var sheetPage = await context.NewPageAsync().WaitAsync(token);
            foreach (var indexes in ContactSheet.Pages(frames.Count, options))
            {
                var html = ContactSheet.BuildHtml(frames, indexes, options);
                await sheetPage.SetContentAsync(html, new PageSetContentOptions
                {
                    WaitUntil = WaitUntilState.Load,
                    Timeout = 5000,
                }).WaitAsync(token);

                sheets.Add(await sheetPage.ScreenshotAsync(new PageScreenshotOptions
                {
                    Type = ScreenshotType.Jpeg,
                    Quality = options.ScreenshotQuality,
                    FullPage = true,
                    Timeout = ScreenshotTimeoutMs,
                }).WaitAsync(token));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Contact sheets could not be made after {Sheets} sheets", sheets.Count);
        }
        finally
        {
            if (context is not null)
            {
                try
                {
                    await context.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception)
                {
                    // Best effort; the context goes away with the browser.
                }
            }
        }

        return sheets;
    }
}
