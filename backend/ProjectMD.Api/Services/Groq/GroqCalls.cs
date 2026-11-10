using Microsoft.Extensions.Options;
using ProjectMD.Api.Services.Ai;

namespace ProjectMD.Api.Services.Groq;

// Every call the app makes to a Groq model lives in this file, one named function per purpose, so
// "where does the AI get called?" has one answer. Nothing else may call ILlmClient.CompleteAsync.
// The callers (PipelineScanGenerator, MotionAnalyzer, ComponentAnalyzer) still own the cache, the
// budget check before the call and the ledger record after it; these functions only make the call.
//
//   WriteProseAsync              text model (Ai:FastModel)       the short explanations in the document
//   DescribeMotionAsync          vision model (Motion:VisionModel) animation notes and design breakdown
//   WriteComponentPreviewsAsync  text model (Ai:FastModel)       HTML/CSS previews of the Components tab
public static class GroqCalls
{
    // The prose call. If the provider says 429/5xx (a transient LlmProviderException) it waits
    // Ai:RetryBackoffMs and tries exactly once more; a second failure is not retried.
    public static async Task<LlmResult> WriteProseAsync(
        this ILlmClient llm,
        LlmRequest request,
        AiOptions options,
        TimeProvider clock,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            return await llm.CompleteAsync(request, ct);
        }
        catch (LlmProviderException ex) when (ex.IsTransient && !ct.IsCancellationRequested)
        {
            logger.LogWarning("AI provider returned a transient error (status {Status}); retrying once", ex.StatusCode);
            if (options.RetryBackoffMs > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(options.RetryBackoffMs), clock, ct);
            }

            return await llm.CompleteAsync(request, ct);
        }
    }

    // The vision call: the request carries the screenshots (see MotionPromptBuilder). No retry here.
    public static Task<LlmResult> DescribeMotionAsync(this ILlmClient llm, LlmRequest request, CancellationToken ct) =>
        llm.CompleteAsync(request, ct);

    // The component-preview call. The caller decides whether to try again (ComponentAnalyzer retries
    // once when Groq refuses the model's JSON with a 400).
    public static Task<LlmResult> WriteComponentPreviewsAsync(this ILlmClient llm, LlmRequest request, CancellationToken ct) =>
        llm.CompleteAsync(request, ct);
}
