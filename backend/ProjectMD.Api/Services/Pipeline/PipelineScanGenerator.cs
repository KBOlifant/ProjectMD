using Microsoft.Extensions.Options;
using ProjectMD.Api.Models;
using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Services.Pipeline;

// The real scan generator, used when Scanner:Mode is `extract` or `ai`.
//
//   extract mode:  fetch + reduce + fingerprint (all free)  ->  deterministic Markdown. No AI.
//   ai mode:       the same free work, then ONE AI call for the short prose parts:
//                  cache lookup -> budget check -> model call -> store cache -> record usage.
//
// Credit-saving rules in play here: the free work is never repeated by the AI; an identical page
// (same content hash, prompt version and model) is served from the cache for 0 credits; a daily
// token cap is checked BEFORE the call; there is exactly one call per scan (at most one retry for a
// provider 429/5xx, never a loop); a failed extraction makes no AI call; and any AI trouble (not
// configured, over budget, call failed) falls back to the deterministic document instead of
// failing the scan or retrying.
public sealed class PipelineScanGenerator : IScanGenerator
{
    private readonly IExtractionService _extraction;
    private readonly ITechFingerprinter _fingerprinter;
    private readonly IExtractionCache _cache;
    private readonly ILlmBudgetGuard _budget;
    private readonly ILlmClient _llm;
    private readonly PromptBuilder _prompts;
    private readonly IScanOwnerProvider _owner;
    private readonly IOptions<ScannerModeOptions> _scanner;
    private readonly IOptions<AiOptions> _ai;
    private readonly TimeProvider _clock;
    private readonly ILogger<PipelineScanGenerator> _logger;

    // Receives the free extraction pieces, the AI pieces, the settings and the clock.
    public PipelineScanGenerator(
        IExtractionService extraction,
        ITechFingerprinter fingerprinter,
        IExtractionCache cache,
        ILlmBudgetGuard budget,
        ILlmClient llm,
        PromptBuilder prompts,
        IScanOwnerProvider owner,
        IOptions<ScannerModeOptions> scanner,
        IOptions<AiOptions> ai,
        TimeProvider clock,
        ILogger<PipelineScanGenerator> logger)
    {
        _extraction = extraction;
        _fingerprinter = fingerprinter;
        _cache = cache;
        _budget = budget;
        _llm = llm;
        _prompts = prompts;
        _owner = owner;
        _scanner = scanner;
        _ai = ai;
        _clock = clock;
        _logger = logger;
    }

    // Produces the scan for an already-validated public URL. An ExtractionException from the free
    // stage is NOT caught here (the controller maps it), and in that case no AI call is made. A
    // BudgetExceededException escapes only when Ai:FallbackOnBudget is false. Duration is the real
    // elapsed time of the whole generation. Pages is always 1: only the one page is fetched.
    public async Task<GeneratedScan> GenerateAsync(Uri url, CancellationToken ct)
    {
        var started = _clock.GetTimestamp();

        // Free work first. If it fails, we stop here and the AI is never called.
        var digest = await _extraction.ExtractAsync(url, ct);
        var tech = _fingerprinter.Detect(digest);

        var (notes, basis) = await WriteNotesAsync(digest, tech, ct);

        var confidence = ConfidenceEstimator.Estimate(digest, tech);
        const int pages = 1;
        var durationMs = (int)Math.Min(int.MaxValue, _clock.GetElapsedTime(started).TotalMilliseconds);
        var markdown = MarkdownDocumentBuilder.Build(
            new ScanFacts(
                UrlValidator.DisplayHost(url),
                url,
                _clock.GetUtcNow().UtcDateTime,
                digest,
                tech,
                confidence,
                durationMs,
                basis),
            notes);

        return new GeneratedScan(durationMs, confidence, pages, tech, markdown);
    }

    // The AI stage. Returns the validated AI notes (or null) and how the document was produced.
    // Never throws for AI trouble; only a budget stop with FallbackOnBudget=false and a cancelled
    // request escape. The cache holds the validated AI JSON, never Markdown.
    private async Task<(AiDocument? Notes, ScanBasis Basis)> WriteNotesAsync(
        PageDigest digest,
        IReadOnlyList<ScanTech> tech,
        CancellationToken ct)
    {
        if (_scanner.Value.Resolve() != ScannerMode.Ai)
        {
            return (null, ScanBasis.Extract);
        }

        // No real client: behave exactly like extract mode (no database work, no wasted effort).
        if (_llm is NotConfiguredLlmClient)
        {
            return (null, ScanBasis.AiNotConfigured);
        }

        var options = _ai.Value;
        LlmRequest request;
        try
        {
            request = _prompts.Build(digest, tech, options);
        }
        catch (InvalidOperationException ex)
        {
            // A settings problem (for example a missing model name). Startup validation normally
            // catches this; if it slipped through, the user still gets the basic document.
            _logger.LogError("AI prompt could not be built: {Reason}", ex.Message);
            return (null, ScanBasis.AiFailed);
        }

        var userId = await _owner.GetUserIdAsync(ct);
        var knownIds = EndpointRanking.KnownIds(digest.Api);

        // 1. Cache: an identical page, prompt and model costs nothing. The stored JSON is validated
        // again, so a damaged or old row is simply a miss.
        var cached = AiDocumentParser.Parse(await TryCacheAsync(digest.ContentHash, request.Model, ct), knownIds);
        if (cached is not null)
        {
            await RecordAsync(userId, new LlmResult(string.Empty, 0, 0, request.Model), cacheHit: true, ct);
            return (cached, ScanBasis.Ai);
        }

        // 2. Budget: refuse BEFORE spending. The estimate is the worst case (full input plus the
        // full output allowance), so a call can never take the day over a cap.
        var estimatedTokens = TokenEstimator.Estimate(request.System)
            + TokenEstimator.Estimate(request.User)
            + options.OutputTokenCap(_llm.AddsReasoningHeadroom);
        try
        {
            await _budget.EnsureAsync(userId, estimatedTokens, ct);
        }
        catch (BudgetExceededException ex) when (options.FallbackOnBudget)
        {
            _logger.LogInformation("AI budget reached ({Scope}); using the basic document", ex.Scope);
            return (null, ScanBasis.AiBudget);
        }

        // 3. The single model call (with at most one retry for a transient provider error).
        LlmResult result;
        try
        {
            result = await CompleteWithOneRetryAsync(request, options, ct);
        }
        catch (LlmNotConfiguredException)
        {
            _logger.LogWarning("AI writing is not configured; using the basic document");
            return (null, ScanBasis.AiNotConfigured);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Not retried again and not shown to the user. Only the exception type is logged:
            // never the prompt, the page content or anything a provider might echo back.
            _logger.LogWarning("AI call failed ({ErrorType}); using the basic document", ex.GetType().Name);
            return (null, ScanBasis.AiFailed);
        }

        // 4. Keep the answer for next time, then write the real usage to the ledger.
        // Invalid JSON, a refusal, an empty or cut-off answer all parse to null: basic document, no retry.
        var notes = AiDocumentParser.Parse(result.Text, knownIds);
        if (notes is not null)
        {
            await StoreCacheAsync(digest.ContentHash, request.Model, AiDocumentParser.ToJson(notes), ct);
        }

        await RecordAsync(userId, result, cacheHit: false, ct);

        return notes is not null ? (notes, ScanBasis.Ai) : (null, ScanBasis.AiFailed);
    }

    // Calls the model. If the provider says 429/5xx (a transient LlmProviderException) it waits
    // Ai:RetryBackoffMs and tries exactly once more; a second failure is not retried.
    private async Task<LlmResult> CompleteWithOneRetryAsync(LlmRequest request, AiOptions options, CancellationToken ct)
    {
        try
        {
            return await _llm.CompleteAsync(request, ct);
        }
        catch (LlmProviderException ex) when (ex.IsTransient && !ct.IsCancellationRequested)
        {
            _logger.LogWarning("AI provider returned a transient error (status {Status}); retrying once", ex.StatusCode);
            if (options.RetryBackoffMs > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(options.RetryBackoffMs), _clock, ct);
            }

            return await _llm.CompleteAsync(request, ct);
        }
    }

    // Cache read that can never fail the scan: any database trouble just counts as a miss.
    private async Task<string?> TryCacheAsync(string contentHash, string model, CancellationToken ct)
    {
        try
        {
            return await _cache.TryGetAsync(contentHash, PromptBuilder.PromptVersion, model, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("AI cache read failed ({ErrorType}); treating it as a miss", ex.GetType().Name);
            return null;
        }
    }

    // Cache write that can never fail the scan.
    private async Task StoreCacheAsync(string contentHash, string model, string aiJson, CancellationToken ct)
    {
        try
        {
            await _cache.StoreAsync(contentHash, PromptBuilder.PromptVersion, model, aiJson, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("AI cache write failed ({ErrorType}); continuing", ex.GetType().Name);
        }
    }

    // Ledger write that can never throw away a result that was already paid for. A failure is
    // logged as an error because the daily caps depend on this ledger.
    private async Task RecordAsync(Guid userId, LlmResult result, bool cacheHit, CancellationToken ct)
    {
        try
        {
            await _budget.RecordAsync(userId, result, cacheHit, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("AI usage could not be recorded ({ErrorType}); the daily caps may be undercounted", ex.GetType().Name);
        }
    }
}
