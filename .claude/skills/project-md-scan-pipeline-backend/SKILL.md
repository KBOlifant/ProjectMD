---
name: project-md-scan-pipeline-backend
description: Use when changing how the backend fetches and reduces a page, the scanner modes, the safety checks on URLs and requests, or the scan endpoints.
---

# Scan Pipeline (backend)

Code: `backend/ProjectMD.Api/Services/` (`Extraction/`, `Pipeline/`, `UrlValidator.cs`, `IScanGenerator.cs`) and `Controllers/ScansController.cs`. Settings: `Scanner` section of `appsettings.json`.

## Modes (`Scanner:Mode`)

- `static`: the mock generator. Nothing is fetched.
- `extract`: real fetch, reduce and fingerprint; the Markdown is built with no AI.
- `ai`: everything in `extract`, then the AI writes the short prose parts. Without a configured AI client it behaves like `extract` and the footer says so. A typo in the value stops the app at startup.

Check `appsettings.json`, `appsettings.Development.json` and `backend/.env` (as `Scanner__Mode`) before assuming which mode is live. The committed value is `static`; an uncommitted edit in the working tree sets it to `api`, which is not a valid mode and would stop the app at startup unless `.env` overrides it.

## Flow of one scan (`PipelineScanGenerator`)

1. `UrlValidator` re-checks the URL (the frontend check is a mirror, not the boundary).
2. `ExtractionService`: `PlaywrightPageFetcher` loads the page in headless Chromium; `HtmlReducer` shrinks it to a `PageDigest` (title, description, outline, text, links, JSON-LD, content hash; no HTML, usually over 90% smaller).
3. Alongside the page content, the scan also captures: `TechFingerprinter` (rules in `TechRules.json`), API-call capture (`Extraction/Api/`), design capture (`Extraction/Design/`), component capture (`Extraction/Components/`), and visual/motion capture (`Extraction/Visual/`).
4. AI stages in `ai` mode (see `../project-md-ai-budget-safety/SKILL.md`).
5. `MarkdownDocumentBuilder` writes the document; `ConfidenceEstimator` scores it; `ScanRecorder` saves the scan if the quota allows.

A page that answers with an HTTP error, or with a bot-check page, is not documented. `BlockDetector` recognises those pages and the scan stops with a plain message. It never tries to get around the protection.

## Safety rules (SSRF and hostile pages)

- The page is untrusted. No page text, HTML or URL goes anywhere it could run.
- `RequestGuard` resolves the host and refuses non-public addresses. `GuardedHttpClient` then connects to the exact addresses the guard checked, so a hostile DNS answer cannot switch to a private address after the check.
- Redirects inside the browser are not seen by Playwright's own interception, so `PageRequestProxy` answers every request itself and treats a redirect as a fresh top-level navigation that is checked again.
- Each fetch uses a fresh browser context (no cookies, storage or cache shared between scans). `Scanner:MaxConcurrentFetches` limits parallel pages; the whole scan has a time limit (`Scanner:OverallTimeoutSeconds`, 75 in the committed file).
- API capture records paths and shapes only, never secrets or values.
- Errors shown to users are safe messages (`ExtractionException`); details go to the log.

## Endpoints

`GET /api/scans`, `GET /api/scans/{id}`, `POST /api/scans`, `DELETE /api/scans` (clear), `GET /api/me`, `GET`/`PUT /api/admin/users`, `GET /api/health`. Errors are ProblemDetails with a `reason`: `url`, `extract`, `quota`, `rate`, `budget`. Swagger is Development only.

## Verification

- `dotnet build` and `dotnet test` on `backend/ProjectMD.sln`. Tests that need a browser or the network are separate (`BrowserTests`, `LiveScanTests`).
- Never start the server from an automated shell (it leaves processes that lock build files); use tests or the live-scan test described in `docs/HANDOFF.md`.
- Any change to the guard or proxy needs a test with a local server that redirects to a private address.
