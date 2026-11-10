using Microsoft.Extensions.Options;
using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Extraction.Visual;
using ProjectMD.Api.Services.Pipeline;
using ProjectMD.Api.Services.Groq;

namespace ProjectMD.Api.Services.Ai;

// Asks the vision model to describe a scanned page's animations and break down its visual design.
// Called by PipelineScanGenerator in `ai` mode, after the main AI call.
public interface IMotionAnalyzer
{
    // Returns the validated notes, or null when motion AI is off, there is nothing to send, or the
    // AI is unavailable, over budget or answered badly. Never throws for AI trouble; only the
    // caller's cancellation escapes.
    Task<MotionNotes?> AnalyzeAsync(PageDigest digest, CancellationToken ct);
}

// The real IMotionAnalyzer. Same credit rules as the main AI stage:
//   cache lookup (same page content, prompt version and vision model = 0 credits)
//   -> budget check BEFORE the call (images counted at Motion:TokensPerImage each)
//   -> one call (at most one retry, inside the Groq client, for a 429/5xx)
//   -> cache the validated answer -> record the real token counts in the ledger.
public sealed class MotionAnalyzer : IMotionAnalyzer
{
    private readonly ILlmClient _llm;
    private readonly IExtractionCache _cache;
    private readonly ILlmBudgetGuard _budget;
    private readonly IScanOwnerProvider _owner;
    private readonly MotionPromptBuilder _prompts;
    private readonly IOptions<MotionOptions> _motion;
    private readonly IOptions<AiOptions> _ai;
    private readonly ILogger<MotionAnalyzer> _logger;

    // Receives the AI client, cache, budget guard, owner, prompt builder, settings and a logger.
    public MotionAnalyzer(
        ILlmClient llm,
        IExtractionCache cache,
        ILlmBudgetGuard budget,
        IScanOwnerProvider owner,
        MotionPromptBuilder prompts,
        IOptions<MotionOptions> motion,
        IOptions<AiOptions> ai,
        ILogger<MotionAnalyzer> logger)
    {
        _llm = llm;
        _cache = cache;
        _budget = budget;
        _owner = owner;
        _prompts = prompts;
        _motion = motion;
        _ai = ai;
        _logger = logger;
    }

    public async Task<MotionNotes?> AnalyzeAsync(PageDigest digest, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(digest);
        var options = _motion.Value;
        var skipReason =
            !options.Enabled ? "Motion:Enabled is false"
            : !options.AiEnabled ? "Motion:AiEnabled is false"
            : _llm is NotConfiguredLlmClient ? "no AI client is configured"
            : digest.Visual is null ? "no screenshots were taken for this page"
            : null;
        if (skipReason is not null)
        {
            _logger.LogInformation("Vision AI skipped: {Reason}", skipReason);
            return null;
        }

        LlmRequest? request;
        try
        {
            request = _prompts.Build(digest.Visual!, digest.Design, options);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError("Motion prompt could not be built: {Reason}", ex.Message);
            return null;
        }

        if (request is null)
        {
            _logger.LogInformation("Vision AI skipped: no screenshots and no motion facts to send");
            return null;
        }

        var userId = await _owner.GetUserIdAsync(ct);

        // 1. Cache.
        var cached = MotionNotesParser.Parse(await TryCacheAsync(digest.ContentHash, request.Model, ct));
        if (cached is not null)
        {
            await RecordAsync(userId, new LlmResult(string.Empty, 0, 0, request.Model), cacheHit: true, ct);
            return cached;
        }

        // 2. Budget, worst case.
        var estimate = MotionPromptBuilder.EstimateTokens(
            request, options, request.MaxOutputTokens + (_llm.AddsReasoningHeadroom ? Math.Max(0, _ai.Value.ReasoningTokenHeadroom) : 0));
        try
        {
            await _budget.EnsureAsync(userId, estimate, ct);
        }
        catch (BudgetExceededException ex)
        {
            _logger.LogInformation("AI budget reached ({Scope}); skipping the motion notes", ex.Scope);
            return null;
        }

        // 3. The call.
        LlmResult result;
        try
        {
            result = await _llm.DescribeMotionAsync(request, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Only the exception type is logged: never the prompt, the images or provider text.
            _logger.LogWarning(
                "Vision AI call failed ({ErrorType}, status {Status}); the document has no animation descriptions or design breakdown",
                ex.GetType().Name, (ex as LlmProviderException)?.StatusCode);
            return null;
        }

        // 4. Cache the validated answer, then record the real usage.
        var notes = MotionNotesParser.Parse(result.Text);
        if (notes is null)
        {
            _logger.LogWarning(
                "Vision AI answer was empty or not usable JSON ({CompletionTokens} completion tokens); the document has no animation descriptions or design breakdown",
                result.CompletionTokens);
        }
        if (notes is not null)
        {
            await StoreCacheAsync(digest.ContentHash, request.Model, MotionNotesParser.ToJson(notes), ct);
        }

        await RecordAsync(userId, result, cacheHit: false, ct);
        return notes;
    }

    private async Task<string?> TryCacheAsync(string contentHash, string model, CancellationToken ct)
    {
        try
        {
            return await _cache.TryGetAsync(contentHash, MotionPromptBuilder.PromptVersion, model, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Motion cache read failed ({ErrorType}); treating it as a miss", ex.GetType().Name);
            return null;
        }
    }

    private async Task StoreCacheAsync(string contentHash, string model, string json, CancellationToken ct)
    {
        try
        {
            await _cache.StoreAsync(contentHash, MotionPromptBuilder.PromptVersion, model, json, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Motion cache write failed ({ErrorType}); continuing", ex.GetType().Name);
        }
    }

    private async Task RecordAsync(Guid userId, LlmResult result, bool cacheHit, CancellationToken ct)
    {
        try
        {
            await _budget.RecordAsync(userId, result, cacheHit, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("Motion AI usage could not be recorded ({ErrorType}); the daily caps may be undercounted", ex.GetType().Name);
        }
    }
}
