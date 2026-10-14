---
name: backend-groq-client
description: Implement the real Groq LLM client behind ILlmClient (model openai/gpt-oss-20b), keeping credit use low and the API key out of logs and git.
---

# Backend Groq client

**Owns:** `backend/ProjectMD.Api/Services/Ai/GroqLlmClient.cs` (+ options), the registration in `Services/ScanServiceCollectionExtensions.cs` / `Services/Ai/*` where the client is chosen, tests under `ProjectMD.Api.Tests/Ai/Groq*`, `backend/.env.example`, README section.
**Builds on:** [19-backend-ai-budget.md](19-backend-ai-budget.md). `ILlmClient`, `PromptBuilder`, the cache, budget guard and fallbacks already exist; do not redesign them.

## What to build
- `GroqLlmClient : ILlmClient` calling Groq's OpenAI-compatible chat completions endpoint: `POST {Ai:BaseUrl}/chat/completions` with `Authorization: Bearer {Ai:ApiKey}`. Defaults: `Ai:BaseUrl = https://api.groq.com/openai/v1`. **Confirm the current request/response field names and the model id from Groq's official docs (https://console.groq.com/docs) before coding**; do not rely on memory. Expected: model id `openai/gpt-oss-20b`; `max_completion_tokens`; `temperature` 0 (`LlmRequest` has no temperature field: add one with default 0 to the record or set it in the client); `reasoning_effort` (`low`) because gpt-oss is a reasoning model whose hidden reasoning tokens are billed as output; usage in `usage.prompt_tokens` / `completion_tokens`.
- **Credit rules:** `Ai:ReasoningEffort` default `low` (config, validated against `low|medium|high`). Send the `LlmRequest.MaxOutputTokens` as the hard cap but allow headroom for reasoning: `Ai:ReasoningTokenHeadroom` (default 400) is added in the client only, and the budget guard must reserve the SAME total so limits stay honest (adjust the guard's reservation to use a shared helper rather than duplicating the number). Ask for no reasoning text back if the API offers a way to hide it. One request per scan, no streaming.
- If the response is cut off (`finish_reason == "length"`) or the text is empty, treat it as a failed AI call: return the deterministic fallback and record the tokens actually used. Do not retry.
- Errors: HTTP 401/403 -> `LlmProviderException` with the safe message "The AI service rejected the server's credentials" (never include the key or response body); 429/5xx -> at most one retry with backoff (reuse `Ai:RetryBackoffMs`, honour `Retry-After` up to 5 s), then fall back. Timeouts 30 s (`Ai:TimeoutSeconds`). Use `IHttpClientFactory` (typed client), no auto-redirects, TLS defaults.
- **Key handling:** read `Ai:ApiKey` from configuration (`backend/.env` as `Ai__ApiKey`, or an environment variable). NEVER log it, the `Authorization` header, request prompts or response bodies at Information or above; log only status code, model, token counts, latency. Do not put it in any tracked file, test fixture, README example (use `YOUR_GROQ_API_KEY` placeholder) or exception message.
- **Selection:** if `Ai:ApiKey` is non-empty AND `Scanner:Mode` is `ai`, register `GroqLlmClient`; otherwise keep `NotConfiguredLlmClient` (the existing "AI writing is not configured" fallback). `Ai:FastModel` must be set when the Groq client is registered (startup validation already exists; make the error name the key). Default `Ai:FastModel` stays empty in `appsettings.json`; the user's `.env` sets it.
- Add `Ai__ApiKey=`, `Ai__FastModel=openai/gpt-oss-20b`, `Scanner__Mode=ai` and `Scanner__BrowserChannel=chrome` (placeholders/comments) to `.env.example`, and update the README (how to set the key, what the model setting means, how credits are saved, how to verify with the `ai` mode).

## Tests (no real network, no real key)
Use a fake `HttpMessageHandler`: request shape (URL, bearer header present but never in logs, model, caps, reasoning effort), parsing of text and usage, truncated/empty response -> fallback, 401 -> provider exception without secrets, 429 then success (one retry), 429 twice -> fallback, timeout, budget reservation includes the headroom, key never appears in captured log output, client not registered when the key is empty or the mode is not `ai`. Add an opt-in live test skipped unless `PROJECTMD_LIVE_GROQ=1` and the key is present in the environment (it must make one tiny call and assert only on shape).

## Rules
Plain-English comment above each class/method. Do NOT read, print, copy or use `backend/.env` or any real key. Do not connect to the real database. Do not run git. `dotnet build ProjectMD.sln` 0 warnings and `dotnet test ProjectMD.sln` pass (sequentially). Report files, config keys with defaults, the exact request/response fields you confirmed from the docs (and the doc URL), test results, anything unresolved.
