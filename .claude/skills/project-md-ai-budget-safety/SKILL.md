---
name: project-md-ai-budget-safety
description: Use when changing the AI calls (Groq), prompts, structured output, the token budget, the answer cache, or how AI text is cleaned before it is shown.
---

# AI Calls, Budget and Cleaning

Code: `backend/ProjectMD.Api/Services/Groq/` (client and every model call), `Services/Ai/` and `Services/Pipeline/`. Settings: `Ai` and `Motion` sections.

## What the AI does

It writes short explanations only, as a small JSON object that follows a schema (`AiDocument`, `PromptBuilder`). Facts (technologies, endpoints, colours, outline) are written by code. API endpoints are given to the model as ids (E1, E2...) so it cannot invent a path. Text is `openai/gpt-oss-20b`-class via Groq's OpenAI-compatible endpoint (`GroqLlmClient`, all calls in `Services/Groq/GroqCalls.cs`); the model names come from configuration (`Ai:FastModel`, `Motion:VisionModel`), never from code.

Calls per scan, at most: the prose call, the vision call for animations (needs screenshots), and the component-preview call.

## Credit rules (do not break these)

1. Free work first; the AI never repeats what code already did.
2. **Cache:** `ExtractionCache` key is content hash + prompt version + model, shared across users, because it holds only public-page-derived text. A hit costs nothing. Bump `PromptVersion` whenever a prompt, answer shape or cleaning rule changes.
3. **Budget before the call:** `LlmBudgetGuard.EnsureAsync` runs before every model call; `RecordAsync` writes the real token counts after. Caps: `Ai:DailyTokenCapPerUser` (60000) and `Ai:DailyTokenCapGlobal` (200000), per UTC day. The check and the record are separate steps, so concurrent requests can overshoot slightly (a known limit).
4. One call per stage, at most one retry for a provider 429 or 5xx. No loops.
5. Any AI trouble (not configured, over budget, failed call, unusable answer) falls back to the deterministic document. With `Ai:FallbackOnBudget=false` an exhausted budget is a 429 with `reason: "budget"`.
6. Input is capped (`Ai:MaxInputTokens` 3000, `TokenEstimator` over-counts on purpose); output is capped (`Ai:MaxOutputTokens` 800).

## Treat the answer as untrusted

The prompt contains untrusted page text, so the answer is untrusted too.

- `AiProseSanitizer`: strips raw HTML, images and links, flattens control characters and newlines, caps length. `MarkdownText.Inline` escapes it again when writing the Markdown, so it cannot add headings, lists, table cells or links.
- `ComponentSanitizer`: allow-lists tags, attributes and CSS properties (see `../project-md-component-snippets/SKILL.md`).
- Animation code is built from numbers and whitelisted words only (see `../project-md-animation-snippets/SKILL.md`).
- Never put the model's text into code, a URL, a file name or a query.

## Secrets

The key (`Ai__ApiKey`) lives in `backend/.env` or the environment. Never print, log, commit or send it to the frontend. A startup notice (`AiModeStartupNotice`) says once whether AI writing is available.

## Verification

- Backend: `AiConfigurationTests`, `GroqLlmClientTests`, `LlmBudgetGuardTests`, `ExtractionCacheTests`, `PromptBuilderTests`, `PipelineScanGeneratorTests`, `AiApiTests`.
- Test with a fake `ILlmClient`. Live calls are small, deliberate, and use the live-scan test only.
- Scan the same page twice and confirm the second makes no AI call.
