---
name: backend-extraction
description: Playwright page fetching, HTML reduction and tech fingerprinting for the ASP.NET Core backend: the free (no AI credits) front half of the scan pipeline.
---

# Backend extraction (fetch, reduce, fingerprint)

**Owns:** `backend/ProjectMD.Api/Services/Extraction/*` and `backend/ProjectMD.Api.Tests/Extraction/*`. Agent 19 owns the orchestrator, AI seam, budget and cache and calls your code through the contracts below.
**Goal:** turn any public URL into a small, clean `PageDigest` with no LLM and no credits. Everything here must work (and be useful) without Groq.

## Modes (`Scanner:Mode` in config, default `static`)
`static` = today's mock (nothing changes). `extract` = real fetch + reduce + fingerprint, output built without AI. `ai` = adds the LLM stage (agent 19; not implemented yet). Your code is only reached in `extract`/`ai`.

## Contract (names and members are fixed; agent 19 codes against them)
```csharp
namespace ProjectMD.Api.Services.Extraction;

public sealed record RawPage(Uri FinalUrl, int StatusCode, string Html,
    IReadOnlyDictionary<string,string> Headers, IReadOnlyList<string> ScriptUrls, IReadOnlyList<string> StylesheetUrls);

public interface IPageFetcher { Task<RawPage> FetchAsync(Uri url, CancellationToken ct); }          // PlaywrightPageFetcher

public sealed record HeadingNode(int Level, string Text);
public sealed record LinkRef(string Text, string Href);
public sealed record PageDigest(Uri FinalUrl, int StatusCode, string? Title, string? MetaDescription,
    string? Language, IReadOnlyList<HeadingNode> Outline, string Text, IReadOnlyList<LinkRef> Links,
    IReadOnlyList<string> ScriptUrls, IReadOnlyList<string> StylesheetUrls,
    IReadOnlyDictionary<string,string> Headers, IReadOnlyList<string> JsonLd,
    int RawHtmlChars, int ReducedChars, int EstimatedTokens, string ContentHash);

public sealed record ReduceOptions(int MaxTextChars = 12000, int MaxLinks = 40, int MaxHeadings = 60);
public interface IHtmlReducer { PageDigest Reduce(RawPage page, ReduceOptions options); }

public interface ITechFingerprinter { IReadOnlyList<ScanTech> Detect(PageDigest digest); }           // uses ProjectMD.Api.Models.ScanTech

public interface IExtractionService { Task<PageDigest> ExtractAsync(Uri url, CancellationToken ct); } // fetch + reduce
public static class ExtractionServiceCollectionExtensions { public static IServiceCollection AddExtraction(this IServiceCollection s, IConfiguration c); }
```
`EstimatedTokens` = `ceil(chars / 4)` of the text that would be sent (document the approximation). `ContentHash` = SHA-256 hex of a normalised form (title + meta + outline + reduced text), stable across runs and independent of volatile bits (timestamps, nonces, CSRF tokens, session ids).

## Fetcher (Playwright for .NET, package `Microsoft.Playwright`)
- One shared headless Chromium (singleton, disposed on shutdown), a fresh browser context per fetch (no cookies/storage), `SemaphoreSlim` concurrency limit (`Scanner:MaxConcurrentFetches`, default 2), navigation timeout 30 s (`WaitUntil.NetworkIdle` with a hard overall 45 s cap), user agent `ProjectMD-Scanner/1.0`, JavaScript enabled (to see rendered DOM), downloads and dialogs refused.
- **SSRF protection is the main security job.** Validate the URL with the existing `IUrlValidator` first. Then guard EVERY request the page makes, including redirects and sub-resources, with `context.RouteAsync("**/*")`: allow only `http`/`https`; resolve the host's DNS (all A/AAAA records) and abort if ANY address is not public (`UrlValidator.IsPublicAddress`); abort if resolving fails. Put the decision in a pure, testable `RequestGuard` (injectable `IDnsResolver`). Block images, fonts and media to save time and bandwidth (they are not needed). Cap the redirect count and the total response bytes you keep (`Scanner:MaxHtmlBytes`, default 2 MB; truncate, do not fail). Record script and stylesheet URLs from the requests (not the bodies).
- Keep only an allowlist of response headers useful for fingerprinting (`server`, `x-powered-by`, `via`, `x-vercel-id`, `cf-ray`, `x-generator`, `content-type`, `set-cookie` NAMES only, never values).
- Errors: wrap into `ExtractionException` with a safe user-facing message (unreachable, timed out, blocked address, not HTML, too large). Never include stack traces or internal addresses in it.
- `robots.txt`: leave a `// TODO(robots)` note only.
- Do NOT download browsers. Document in the README how to install them (`pwsh .../playwright.ps1 install chromium`, or the Playwright CLI) and have `PlaywrightPageFetcher` throw a clear `ExtractionException("The page renderer is not installed on the server")` if the browser is missing. If Chromium is already present on this machine you may smoke-test once against `https://example.com`; otherwise skip and say so.

## Reducer (aim: 90%+ fewer characters than raw HTML)
Use AngleSharp or HtmlAgilityPack (pick one, justify in a comment). Remove: `script`, `style`, `noscript`, `svg`, `canvas`, `iframe`, `template`, comments, `hidden`/`aria-hidden` elements, inline `data:` URIs, and all attributes except `href`, `alt`, `title`, `lang`, `content`/`name`/`property` on meta. Keep: title, meta description, `og:*`, `lang`, JSON-LD blocks (parsed and compacted: valuable and small), the heading outline (h1-h4, capped), visible text of paragraphs, list items and table cells in document order, link text + href (dedupe by href, drop `javascript:`/`mailto:` noise, resolve to absolute, cap). Collapse whitespace, drop duplicate blocks (repeated nav/footer text seen twice), drop lines under 3 characters and cookie-banner phrases. Prefer `main`/`article` content when present. Truncate at `MaxTextChars` by priority (never mid-word). No LLM, no network.

## Fingerprinter (deterministic, data-driven)
A JSON rules file (embedded resource) mapping technology name -> signals: script/stylesheet URL patterns, header patterns, meta generator, HTML/text markers, cookie names. Cover about 40 common ones (React, Next.js, Vue, Nuxt, Angular, Svelte, WordPress, Shopify, Webflow, Wix, Squarespace, Tailwind CSS, Bootstrap, jQuery, Vercel, Netlify, Cloudflare, Fastly, AWS CloudFront, Google Analytics, Google Tag Manager, Segment, PostHog, Hotjar, Stripe, Intercom, HubSpot, Algolia, Clerk, Firebase, Sentry, etc.). Return `ScanTech { Name, Highlighted }` with at most 8 items; highlight the framework/host (first 2). Confidence for each signal is a small score; require at least one strong signal.

## Tests
Fixture HTML strings in the tests (no network, no browser): reducer removes noise and keeps outline/JSON-LD/links; size ratio; stable `ContentHash` across a nonce change; truncation priority; fingerprint hits and misses; `RequestGuard` with a fake resolver (private, loopback, link-local, mixed public+private records, non-http scheme, DNS failure, redirect to a private host). Browser-dependent tests, if any, must be skipped unless an env var is set.

## Rules
Plain-English comment above each class/method. No Groq/LLM code, no keys, no secrets, no real database, no git. `dotnet build` 0 warnings and `dotnet test` pass (run sequentially).
