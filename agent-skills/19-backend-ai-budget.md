---
name: backend-ai-budget
description: The AI half of the scan pipeline, built to spend as few Groq credits as possible: orchestrator, compact prompt builder, token budgets, cache, usage ledger and the (unimplemented) LLM client seam.
---

# Backend AI pipeline and credit budget

**Owns:** `backend/ProjectMD.Api/Services/Ai/*`, `Services/Pipeline/*`, `Models/ExtractionCacheEntry.cs`, `Models/LlmUsageRecord.cs`, `Data/AppDbContext.cs` (new sets only), the new migration, `Services/ScanServiceCollectionExtensions.cs`, the error mapping in `Controllers/ScansController.cs`, tests under `ProjectMD.Api.Tests/Ai/*`.
**Depends on:** [18-backend-extraction.md](18-backend-extraction.md) contract (`IExtractionService`, `PageDigest`, `ITechFingerprinter`, `AddExtraction`). Agent 18 builds those in parallel; code to the contract.
**Hard rule:** do NOT write any Groq HTTP code, SDK use or key handling. `ILlmClient` is a seam with a "not configured" implementation only.

## Strategy to spend fewer credits (in order of savings)
1. **Do the free work without the AI.** Tech list, outline, headings, links, meta, page count and structure come from agent 18's deterministic code. The AI only writes the short prose parts (overview, what the site does, notable sections). In `extract` mode the Markdown is built entirely without the AI (`DeterministicMarkdownBuilder`) and is the fallback if the AI is off, over budget or failing.
2. **Send a small, ranked prompt.** `PromptBuilder` takes the `PageDigest` and builds the input in priority order (title, meta, JSON-LD, outline, main text, top links), stops at `Ai:MaxInputTokens` (default 3000) and never exceeds it. Never send raw HTML, scripts, CSS, base64, screenshots or the full link list. Tech names are passed as a short list, so the model does not re-detect them.
3. **One call, not many.** Single request per scan. If the page is too big, truncate by priority; do not chunk-and-summarise (that multiplies calls).
4. **Cap the output.** `Ai:MaxOutputTokens` (default 700) as a hard `max_tokens`; ask for compact bullet facts in a fixed short template, not essays; temperature 0.
5. **Cache identical work.** Look up `ContentHash + PromptVersion + Model` in `ExtractionCache` before calling the AI. Hit = zero credits (the cached prose is reused for any user; only public page content is stored, never user data). TTL `Ai:CacheDays` (default 7). Bump `PromptVersion` (a constant) whenever the prompt changes.
6. **Cheap model first.** Two configurable model names, `Ai:FastModel` (default for everything) and `Ai:QualityModel` (used only if a future option asks for it). Never hard-code model ids in code; read from config with an empty default, and fail with a clear message if the AI mode is on and they are missing.
7. **Keep the prompt prefix identical.** Put the fixed instructions first and the page content last, so provider-side prompt caching (where offered) can apply. Do not put user names, timestamps or ids in the prompt.
8. **Hard spending limits.** Record real token counts from the provider response in `LlmUsage`. Before calling, `ILlmBudgetGuard.EnsureAsync(userId, estimatedTokens)` checks `Ai:DailyTokenCapPerUser` (default 20000) and `Ai:DailyTokenCapGlobal` (default 200000). Over a cap: throw `BudgetExceededException`; the controller returns 429 ProblemDetails with `reason: "budget"` and a friendly detail, and the scan falls back to the deterministic Markdown only if `Ai:FallbackOnBudget` is true (default true; then no error is shown and the scan is marked as "basic" in its Markdown footer). Handle provider 429/5xx with at most one retry with backoff, never a loop.
9. **Don't pay for failures.** If extraction fails, no AI call is made. If the AI call fails, return the deterministic Markdown and do not retry automatically.

## Contract and pieces
```csharp
namespace ProjectMD.Api.Services.Ai;
public sealed record LlmRequest(string System, string User, string Model, int MaxOutputTokens);
public sealed record LlmResult(string Text, int PromptTokens, int CompletionTokens, string Model);
public interface ILlmClient { Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct); }
public sealed class NotConfiguredLlmClient : ILlmClient { /* throws LlmNotConfiguredException */ }
public interface ILlmBudgetGuard { Task EnsureAsync(Guid userId, int estimatedTokens, CancellationToken ct); Task RecordAsync(Guid userId, LlmResult result, bool cacheHit, CancellationToken ct); }
public sealed class BudgetExceededException : Exception { }
public sealed class PromptBuilder { LlmRequest Build(PageDigest digest, IReadOnlyList<ScanTech> tech, AiOptions options); }
public sealed class AiOptions { /* MaxInputTokens, MaxOutputTokens, FastModel, QualityModel, CacheDays, DailyTokenCapPerUser, DailyTokenCapGlobal, FallbackOnBudget */ }
```
- `Services/Pipeline/PipelineScanGenerator : IScanGenerator`: fetch+reduce (`IExtractionService`) -> fingerprint -> in `extract` mode build the deterministic Markdown; in `ai` mode: cache lookup -> budget check -> `ILlmClient` -> store cache -> record usage. Produces `GeneratedScan` (real duration, pages = 1 unless a later task crawls more, confidence derived from signals found, never a made-up number presented as measured: label heuristics in comments). Registered when `Scanner:Mode` is `extract` or `ai`; `static` keeps `StaticScanGenerator`. Selecting `ai` with `NotConfiguredLlmClient` must not crash at startup: it logs once and behaves like `extract` with the Markdown footer "AI writing is not configured".
- `ScanServiceCollectionExtensions.AddScanServices` calls agent 18's `AddExtraction(...)`, binds `AiOptions` and registers everything. Validate options (positive caps, model names required only when the AI client is a real one).
- Tables (EF models, new migration named `AddAiPipeline`; do NOT apply it, do not connect to any real database, use the dummy-connection-string env var recipe in backend/README.md): `ExtractionCache(ContentHash varchar(64), PromptVersion int, Model varchar(100) as a composite unique key, Markdown text, CreatedAt, ExpiresAt, HitCount int)` and `LlmUsage(Id, UserId FK cascade, ScanId nullable, Model, PromptTokens, CompletionTokens, CacheHit bool, CreatedAt)` with an index on `(UserId, CreatedAt)`. Neither stores page HTML.
- Controller: map `ExtractionException` to 400/502 ProblemDetails (`reason: "extract"`, safe message), `BudgetExceededException` to 429 (`reason: "budget"`). Keep every existing response unchanged.

## Tests (`ProjectMD.Api.Tests/Ai/`)
Prompt builder never exceeds `MaxInputTokens` and keeps priority order; prompt prefix is byte-identical across different pages; cache hit skips the LLM (fake client counts calls = 0) and records `CacheHit`; budget guard blocks at the user and global caps and counts only the current UTC day; fallback to deterministic Markdown when over budget / client not configured / client throws; single LLM call per scan; no LLM call when extraction fails. Use SQLite/in-memory and fakes. No real network, no real database.

## Rules
Plain-English comment above each class/method. No secrets, no keys in files, no git. `dotnet build` 0 warnings and `dotnet test` pass (sequentially). Update `backend/README.md` (scanner modes, AI settings with their defaults, how credits are saved) and keep it free of secrets.
