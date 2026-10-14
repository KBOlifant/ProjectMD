using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Pipeline;

namespace ProjectMD.Api.Tests.Ai;

// Tests for PipelineScanGenerator: the free stage, the cache, the budget, the single AI call and
// every fallback. All fakes and an in-memory SQLite database: no network, no real database, no model.
public class PipelineScanGeneratorTests
{
    private const string Prose = "## Summary\nAcme runs apps near users.\n## What the site does\n- Hosts apps";

    // Extract mode never asks the AI anything and says so in the footer.
    [Fact]
    public async Task Extract_mode_builds_the_document_without_any_ai_call()
    {
        var llm = FakeLlmClient.Answering(Prose);
        using var h = new PipelineHarness(ScannerMode.Extract, llm);

        var scan = await h.ScanAsync();

        Assert.Equal(0, llm.Calls);
        Assert.Equal(1, h.Extraction.Calls);
        Assert.Contains(DeterministicMarkdownBuilder.FooterExtract, scan.Markdown);
        Assert.DoesNotContain("About this site", scan.Markdown);
        Assert.Empty(h.UsageRows());
    }

    // The deterministic document holds the free facts: overview, tech, outline, links.
    [Fact]
    public async Task Document_contains_the_free_facts()
    {
        using var h = new PipelineHarness(ScannerMode.Extract, null);

        var scan = await h.ScanAsync();

        Assert.StartsWith("# acme.example Documentation\n", scan.Markdown);
        Assert.Contains("## Overview", scan.Markdown);
        Assert.Contains("- **Pages documented:** 1", scan.Markdown);
        Assert.Contains("## Tech Stack", scan.Markdown);
        Assert.Contains("- **Next.js**", scan.Markdown);
        Assert.Contains("- Tailwind CSS", scan.Markdown);
        Assert.Contains("## Site Structure", scan.Markdown);
        Assert.Contains("  - Pricing", scan.Markdown);
        Assert.Contains("[Pricing](https://acme.example/pricing)", scan.Markdown);
        Assert.DoesNotContain("\r", scan.Markdown);
        Assert.Equal(1, scan.Pages);
        Assert.Equal(3, scan.Tech.Count);
    }

    // The scan reports real elapsed time, a heuristic confidence in range, and one page.
    [Fact]
    public async Task Scan_metadata_is_real_and_in_range()
    {
        using var h = new PipelineHarness(ScannerMode.Extract, null);

        var scan = await h.ScanAsync();

        Assert.InRange(scan.DurationMs, 0, 60_000);
        Assert.InRange(scan.Confidence, 0, 100);
        Assert.Equal(ConfidenceEstimator.Estimate(Digests.Simple(), scan.Tech), scan.Confidence);
        Assert.Contains($"{scan.Confidence}%", scan.Markdown);
        Assert.Contains("rough estimate", scan.Markdown);
    }

    // ai mode with the only client that exists (not configured): behaves like extract, no database
    // work, and the footer says the AI is not configured.
    [Fact]
    public async Task Ai_mode_with_no_client_behaves_like_extract()
    {
        using var h = new PipelineHarness(ScannerMode.Ai, null);

        var scan = await h.ScanAsync();

        Assert.Contains("AI writing is not configured", scan.Markdown);
        Assert.Contains(DeterministicMarkdownBuilder.FooterNotConfigured, scan.Markdown);
        Assert.Empty(h.UsageRows());
    }

    // A client that reports "not configured" itself also falls back.
    [Fact]
    public async Task Client_that_says_not_configured_falls_back()
    {
        var llm = FakeLlmClient.Throwing(new LlmNotConfiguredException());
        using var h = new PipelineHarness(ScannerMode.Ai, llm);

        var scan = await h.ScanAsync();

        Assert.Equal(1, llm.Calls);
        Assert.Contains(DeterministicMarkdownBuilder.FooterNotConfigured, scan.Markdown);
    }

    // The happy path: ONE call, prose in the document, real tokens in the ledger, answer cached.
    [Fact]
    public async Task Ai_mode_makes_a_single_call_and_records_usage()
    {
        var llm = FakeLlmClient.Answering(Prose, prompt: 812, completion: 140);
        using var h = new PipelineHarness(ScannerMode.Ai, llm);

        var scan = await h.ScanAsync();

        Assert.Equal(1, llm.Calls);
        Assert.Equal("test-fast", llm.Requests[0].Model);
        Assert.Equal(700, llm.Requests[0].MaxOutputTokens);
        Assert.Contains("## About this site", scan.Markdown);
        Assert.Contains("Acme runs apps near users.", scan.Markdown);
        Assert.Contains(DeterministicMarkdownBuilder.FooterAi, scan.Markdown);

        var row = Assert.Single(h.UsageRows());
        Assert.Equal(h.User.Id, row.UserId);
        Assert.Equal(812, row.PromptTokens);
        Assert.Equal(140, row.CompletionTokens);
        Assert.False(row.CacheHit);
        Assert.Null(row.ScanId);

        using var db = h.Db.CreateContext();
        Assert.Single(db.ExtractionCache.ToList());
    }

    // The prompt sent to the model respects the input budget.
    [Fact]
    public async Task The_request_sent_to_the_model_respects_the_input_budget()
    {
        var llm = FakeLlmClient.Answering(Prose);
        using var h = new PipelineHarness(ScannerMode.Ai, llm, Digests.Huge(), ai => ai.MaxInputTokens = 1200);

        await h.ScanAsync();

        var request = llm.Requests.Single();
        Assert.True(TokenEstimator.Estimate(request.System) + TokenEstimator.Estimate(request.User) <= 1200);
    }

    // An identical page the second time is a cache hit: zero model calls and a CacheHit ledger row.
    [Fact]
    public async Task Cache_hit_skips_the_model_and_is_recorded()
    {
        var llm = FakeLlmClient.Answering(Prose);
        using var h = new PipelineHarness(ScannerMode.Ai, llm);

        var first = await h.ScanAsync();
        var second = await h.ScanAsync();

        Assert.Equal(1, llm.Calls);
        Assert.Contains("Acme runs apps near users.", second.Markdown);
        Assert.Contains("## About this site", first.Markdown);

        var rows = h.UsageRows();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, r => r.CacheHit);
        var hit = rows.Single(r => r.CacheHit);
        Assert.Equal(0, hit.PromptTokens);
        Assert.Equal(0, hit.CompletionTokens);
    }

    // The cache is shared between users: a different user scanning the same content spends nothing.
    [Fact]
    public async Task Cache_is_shared_between_users_for_identical_content()
    {
        var llm = FakeLlmClient.Answering(Prose);
        using var h = new PipelineHarness(ScannerMode.Ai, llm);
        var other = h.Db.AddUser("someone-else");

        await h.ScanAsync();
        await h.ScanAsync(other.Id);

        Assert.Equal(1, llm.Calls);
        Assert.Contains(h.UsageRows(), r => r.UserId == other.Id && r.CacheHit);
    }

    // Different page content (different hash) is a miss and costs one more call.
    [Fact]
    public async Task Different_content_is_a_cache_miss()
    {
        var llm = FakeLlmClient.Answering(Prose);
        var hash = "hash-one";
        using var h = new PipelineHarness(ScannerMode.Ai, llm, digestFactory: () => Digests.Simple(hash));

        await h.ScanAsync();
        hash = "hash-two";
        await h.ScanAsync();

        Assert.Equal(2, llm.Calls);
    }

    // Over the user's cap with fallback on (the default): no model call, the basic document, no error.
    [Fact]
    public async Task Over_budget_falls_back_to_the_basic_document()
    {
        var llm = FakeLlmClient.Answering(Prose);
        using var h = new PipelineHarness(ScannerMode.Ai, llm, configure: ai => ai.DailyTokenCapPerUser = 100);

        var scan = await h.ScanAsync();

        Assert.Equal(0, llm.Calls);
        Assert.Contains(DeterministicMarkdownBuilder.FooterBudget, scan.Markdown);
        Assert.DoesNotContain("About this site", scan.Markdown);
        Assert.Empty(h.UsageRows());
    }

    // The global cap does the same.
    [Fact]
    public async Task Over_the_global_cap_falls_back_too()
    {
        var llm = FakeLlmClient.Answering(Prose);
        using var h = new PipelineHarness(ScannerMode.Ai, llm, configure: ai => ai.DailyTokenCapGlobal = 100);

        var scan = await h.ScanAsync();

        Assert.Equal(0, llm.Calls);
        Assert.Contains("Basic scan", scan.Markdown);
    }

    // With Ai:FallbackOnBudget = false the budget stop reaches the caller (the controller answers 429).
    [Fact]
    public async Task Over_budget_without_fallback_throws()
    {
        var llm = FakeLlmClient.Answering(Prose);
        using var h = new PipelineHarness(ScannerMode.Ai, llm, configure: ai =>
        {
            ai.DailyTokenCapPerUser = 100;
            ai.FallbackOnBudget = false;
        });

        var error = await Assert.ThrowsAsync<BudgetExceededException>(() => h.ScanAsync());

        Assert.Equal(BudgetScope.User, error.Scope);
        Assert.Equal(0, llm.Calls);
    }

    // Tokens already spent today count: each call reserves its worst case (the whole prompt plus the
    // full output allowance, about 1000 tokens for this small page), so with a 2500 cap and 1000
    // really spent per call, the third scan of different content is stopped by the cap.
    [Fact]
    public async Task Spending_accumulates_across_scans_until_the_cap()
    {
        var llm = FakeLlmClient.Answering(Prose, prompt: 900, completion: 100);
        var hash = "a";
        using var h = new PipelineHarness(
            ScannerMode.Ai,
            llm,
            configure: ai => ai.DailyTokenCapPerUser = 2500,
            digestFactory: () => Digests.Simple(hash));

        await h.ScanAsync(); // spent 0 + about 1000 reserved <= 2500; really spends 1000
        hash = "b";
        var second = await h.ScanAsync(); // spent 1000 + about 1000 <= 2500; really spends 1000
        hash = "c";
        var third = await h.ScanAsync(); // spent 2000 + about 1000 > 2500: stopped

        Assert.Equal(2, llm.Calls);
        Assert.Contains("About this site", second.Markdown);
        Assert.Contains(DeterministicMarkdownBuilder.FooterBudget, third.Markdown);
    }

    // A failing client: the basic document, one call, no automatic retry, nothing recorded.
    [Fact]
    public async Task Client_failure_falls_back_without_retrying()
    {
        var llm = FakeLlmClient.Throwing(new InvalidOperationException("boom"));
        using var h = new PipelineHarness(ScannerMode.Ai, llm);

        var scan = await h.ScanAsync();

        Assert.Equal(1, llm.Calls);
        Assert.Contains(DeterministicMarkdownBuilder.FooterFailed, scan.Markdown);
        Assert.Empty(h.UsageRows());
        using var db = h.Db.CreateContext();
        Assert.Empty(db.ExtractionCache.ToList());
    }

    // A transient provider error gets exactly one retry, and a second failure is not retried again.
    [Fact]
    public async Task Transient_error_is_retried_exactly_once()
    {
        var llm = FakeLlmClient.Throwing(new LlmProviderException("rate limited", 429, isTransient: true));
        using var h = new PipelineHarness(ScannerMode.Ai, llm);

        var scan = await h.ScanAsync();

        Assert.Equal(2, llm.Calls);
        Assert.Contains(DeterministicMarkdownBuilder.FooterFailed, scan.Markdown);
    }

    // A transient error followed by success uses the second answer (two calls, one ledger row).
    [Fact]
    public async Task Transient_error_then_success_uses_the_retry()
    {
        var llm = new FakeLlmClient((call, request) => call == 1
            ? throw new LlmProviderException("unavailable", 503, isTransient: true)
            : new LlmResult(Prose, 700, 100, request.Model));
        using var h = new PipelineHarness(ScannerMode.Ai, llm);

        var scan = await h.ScanAsync();

        Assert.Equal(2, llm.Calls);
        Assert.Contains("About this site", scan.Markdown);
        Assert.Single(h.UsageRows());
    }

    // A non-transient provider error (for example 400) is not retried.
    [Fact]
    public async Task Non_transient_provider_error_is_not_retried()
    {
        var llm = FakeLlmClient.Throwing(new LlmProviderException("bad request", 400, isTransient: false));
        using var h = new PipelineHarness(ScannerMode.Ai, llm);

        await h.ScanAsync();

        Assert.Equal(1, llm.Calls);
    }

    // An empty or unusable answer is treated as a failure but its tokens are still recorded.
    [Fact]
    public async Task Empty_answer_falls_back_but_the_spend_is_recorded()
    {
        var llm = FakeLlmClient.Answering("   ", prompt: 500, completion: 5);
        using var h = new PipelineHarness(ScannerMode.Ai, llm);

        var scan = await h.ScanAsync();

        Assert.Contains(DeterministicMarkdownBuilder.FooterFailed, scan.Markdown);
        var row = Assert.Single(h.UsageRows());
        Assert.Equal(500, row.PromptTokens);
        using var db = h.Db.CreateContext();
        Assert.Empty(db.ExtractionCache.ToList());
    }

    // The model's answer is cleaned before use: HTML is stripped from the document.
    [Fact]
    public async Task Model_output_is_sanitised_in_the_document()
    {
        var llm = FakeLlmClient.Answering("## Summary\nHello <script>alert(1)</script>world [x](javascript:alert(1))");
        using var h = new PipelineHarness(ScannerMode.Ai, llm);

        var scan = await h.ScanAsync();

        Assert.DoesNotContain("<script>", scan.Markdown);
        Assert.DoesNotContain("javascript:", scan.Markdown);
    }

    // A failed extraction makes no AI call, touches no budget and writes no ledger row.
    [Fact]
    public async Task Failed_extraction_makes_no_ai_call()
    {
        var llm = FakeLlmClient.Answering(Prose);
        using var h = new PipelineHarness(
            ScannerMode.Ai,
            llm,
            digestFactory: () => throw new ExtractionException("The page could not be reached."));

        await Assert.ThrowsAsync<ExtractionException>(() => h.ScanAsync());

        Assert.Equal(0, llm.Calls);
        Assert.Empty(h.UsageRows());
    }

    // A cancelled request is not swallowed as an "AI failure".
    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        var llm = new FakeLlmClient((_, _) => throw new OperationCanceledException());
        using var h = new PipelineHarness(ScannerMode.Ai, llm);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.CreateGenerator().GenerateAsync(new Uri("https://acme.example/"), cts.Token));
    }

    // With the cache off every scan of the same page pays again.
    [Fact]
    public async Task Cache_off_means_every_scan_calls_the_model()
    {
        var llm = FakeLlmClient.Answering(Prose);
        using var h = new PipelineHarness(ScannerMode.Ai, llm, configure: ai => ai.CacheDays = 0);

        await h.ScanAsync();
        await h.ScanAsync();

        Assert.Equal(2, llm.Calls);
    }
}
