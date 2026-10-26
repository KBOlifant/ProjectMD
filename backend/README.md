# Project MD backend

ASP.NET Core (.NET 10) Web API with EF Core 10 and PostgreSQL. The frontend talks to it over
JSON with a Firebase ID token as a bearer token.

```
backend/
  ProjectMD.sln
  .config/dotnet-tools.json   local tool: dotnet-ef (matches the EF Core major version)
  ProjectMD.Api/
    Program.cs                composition root (marked blocks for auth and scans)
    Models/                   AppUser, Scan, ScanTech, ExtractionCacheEntry, LlmUsageRecord
    Data/                     AppDbContext, design-time factory, Migrations/
    Services/Ai/              AI half of the pipeline: options, prompt builder, LLM seam, budget guard, cache
    Services/Pipeline/        PipelineScanGenerator, deterministic Markdown builder, scanner modes
    Dtos/                     ScanDto, TechDto, CreateScanRequest, UsageDto, MeDto
    Controllers/              HealthController (more are added by the auth and scans work)
    Auth/  Services/          reserved for the auth and scans work
```

## Run

Prerequisite: the .NET 10 SDK.

```powershell
cd backend
dotnet tool restore                # installs the pinned dotnet-ef
dotnet build
dotnet run --project ProjectMD.Api # listens on http://localhost:5080 in Development
```

The API refuses to start until a database connection string is configured (see below).

Check it is up: `GET http://localhost:5080/api/health` returns `{ "status": "ok" }`.
`GET /api/health?db=true` also checks the database and returns `503` if it cannot be reached.

## Configuration and secrets

`ProjectMD.Api/appsettings.json` is committed and holds no secrets:

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Default` | empty | PostgreSQL connection string (set it as a secret, see below) |
| `Firebase:ProjectId` | `project-md-51f14` | Used to validate Firebase ID tokens |
| `Cors:AllowedOrigins` | `["http://localhost:3000"]` | Browser origins allowed to call the API |
| `Auth:AdminEmails` | `[]` | Bootstrap admin emails, set as a secret (see [Roles in the database](#roles-in-the-database)) |
| `Auth:DefaultRole` | `member` | Role given to new users (`viewer` or `member`) |
| `Scanner:Mode` | `static` | `static` (mock), `extract` (real fetch, no AI) or `ai` (see [Scanner modes](#scanner-modes-and-ai-credits)) |
| `Ai:MaxInputTokens` | `3000` | Hard ceiling for the prompt sent to the model (estimated tokens) |
| `Ai:MaxOutputTokens` | `800` | Hard cap for the structured JSON answer |
| `Ai:FastModel` | empty | Cheap model used for everything. Required only once a real AI client exists |
| `Ai:QualityModel` | empty | Dearer model, only for a future option that asks for it |
| `Ai:CacheDays` | `7` | How long an AI answer is reused for identical page content (`0` = cache off) |
| `Ai:DailyTokenCapPerUser` | `20000` | Tokens (input + output) one user may spend per UTC day |
| `Ai:DailyTokenCapGlobal` | `200000` | Tokens all users together may spend per UTC day |
| `Ai:FallbackOnBudget` | `true` | Over a cap: `true` = finish with the basic AI-free document, `false` = `429` with `reason: "budget"` |
| `Ai:RetryBackoffMs` | `1000` | Pause before the single retry after a provider 429/5xx (a `Retry-After` header up to 5 s wins) |
| `Ai:ApiKey` | empty | Groq API key. Secret: only in `backend/.env` (`Ai__ApiKey`) or a real environment variable. Empty = no real AI client |
| `Ai:BaseUrl` | `https://api.groq.com/openai/v1` | Groq's OpenAI-compatible API root |
| `Ai:ReasoningEffort` | `low` | `low`, `medium` or `high` (gpt-oss reasoning effort; hidden reasoning tokens are billed as output) |
| `Ai:ReasoningTokenHeadroom` | `400` | Extra output tokens allowed for hidden reasoning on top of `Ai:MaxOutputTokens`; the budget reserves the same total |
| `Ai:TimeoutSeconds` | `30` | Give up on the provider after this long |

Never put a real connection string, password or token in a tracked file, a script, this README
or a log.

### Connection string

Development: store it in [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
(kept outside the repo, per developer):

```powershell
dotnet user-secrets set "ConnectionStrings:Default" "<npgsql connection string>" --project ProjectMD.Api
```

Other environments: set the environment variable `ConnectionStrings__Default` (two underscores).
List values use indexes, for example `Cors__AllowedOrigins__0=https://app.example.com`.

Npgsql connection string format (placeholders only):

```
Host=<host>;Port=<port>;Database=<database>;Username=<user>;Password=<password>;SSL Mode=Require
```

Local overrides that must not be committed can go in `appsettings.Development.local.json`
(ignored by git) but user-secrets is preferred.

## Database migrations

The `dotnet ef` tool is pinned in `.config/dotnet-tools.json`. All commands run from `backend/`
and use `ProjectMD.Api` as both the project and the startup project:

```powershell
dotnet tool restore

# Create a new migration after changing the models

> **Build output** goes to `backend/artifacts/` (not each project's `bin/` and `obj/`). If an older copy of the server is still running or stuck, it keeps its old files locked, and building into the same folder fails with "file is locked by ProjectMD.Api.exe"; a separate output folder avoids that. If `dotnet run` ever serves old behaviour, make sure no other copy is still listening on port 5080.


> **Testing endpoints by hand (Swagger):** start the server with `dotnet run` and open http://localhost:5080/swagger (Development only; it is off in Production). `GET /api/health` needs no sign-in. For the others, sign in on the website (run with `npm run dev`, which shows a **Copy API token** button in the account menu), then in Swagger click **Authorize** and paste the token. Tokens last about an hour; copy a new one when you get 401.


> **Running it:** from the `backend` folder just type `dotnet run`. A small launcher (`Run.csproj`) forwards to the API project and starts it on http://localhost:5080. To build or test, name the solution: `dotnet build ProjectMD.sln` and `dotnet test ProjectMD.sln` (a bare `dotnet build` in this folder is ambiguous). If a build says a file is locked by `ProjectMD.Api.exe`, an earlier copy of the server is still running: stop it (Ctrl+C in its terminal, or `taskkill /F /IM ProjectMD.Api.exe`) and try again.


> **Private settings now live in `backend/.env`** (ignored by git; copy `backend/.env.example`). It holds `ConnectionStrings__Default` and `Auth__AdminEmails__0`. Real environment variables override it, and it works in any environment (Development or Production). Wherever this README mentions user-secrets, the `.env` file now does the same job.

dotnet ef migrations add <Name> --project ProjectMD.Api --output-dir Data/Migrations

# Review the SQL before applying anything
dotnet ef migrations script --project ProjectMD.Api

# Apply to the database named by ConnectionStrings:Default (user-secrets / env var)
dotnet ef database update --project ProjectMD.Api
```

`InitialCreate` creates the `AppUsers` and `Scans` tables (including `Scans.DeletedAt`).
`AddAiPipeline` adds the `ExtractionCache` and `LlmUsage` tables for the AI pipeline. `AddUserRole`
adds `AppUsers.Role` (`varchar(20) NOT NULL DEFAULT 'member'`; existing users become members).
`AddScanAnalysis` adds `Scans.Analysis` (`jsonb`, nullable; old scans stay null). It holds
`{ version, design, api }` (camelCase JSON of the captured `DesignSystem` and `ApiCatalog`), is capped
at 64 KB (response shapes are dropped first, then endpoints), has secret-looking CSS variables removed,
and is returned as `analysis` on every scan. Later features add `components` and `animations` next to
them; readers ignore unknown properties. Static mode attaches a small sample design. Migrations
have to be reviewed and applied deliberately; the API never migrates the database on startup.

### Generating a migration without a database

Creating a migration or a SQL script does not open a connection, but the design-time factory
(`Data/DesignTimeDbContextFactory.cs`) still needs some connection string. Give it a dummy local
value through an environment variable for that one command only:

```powershell
$env:ConnectionStrings__Default = "Host=localhost;Port=1;Database=dummy;Username=dummy;Password=not-a-real-password"
dotnet ef migrations add <Name> --project ProjectMD.Api --output-dir Data/Migrations
$env:ConnectionStrings__Default = $null
```

### Live scan check (opt-in)

`Live_scan_of_public_site_writes_analysis` scans a real public site in-process (no server, no real
database) and writes `scan.md` and `analysis.json`. It is skipped unless `PROJECTMD_LIVE_SCAN=1`.
Optional settings, all from the environment: `PROJECTMD_LIVE_URL` (default `https://tailwindcss.com`),
`PROJECTMD_LIVE_OUT` (output folder), `Scanner__BrowserChannel` (for example `chrome`), and
`Ai__ApiKey` / `Ai__FastModel` for the AI text (without a key it runs in extract mode). The key is
never printed or written.

## Data model

- `AppUsers`: `Id` (guid, PK), `FirebaseUid` (unique, max 128), `Email` (max 320), `CreatedAt`,
  `LastSeenAt` (both `timestamptz`, UTC), `Role` (`viewer` | `member` | `admin`, max 20, default
  `member`; added by the `AddUserRole` migration).
- `Scans`: `Id`, `UserId` (FK to `AppUsers`, cascade delete), `Url` (max 2048), `Host` (max 255),
  `CreatedAt`, `DurationMs`, `Confidence` (0-100), `Pages`, `Tech` (`jsonb` array of
  `{ Name, Highlighted }`), `Markdown` (`text`), `DeletedAt` (`timestamptz`, nullable, UTC; null
  while the scan is visible). Index on `(UserId, CreatedAt DESC)`.
- `ExtractionCache`: composite primary key `(ContentHash varchar(64), PromptVersion int, Model
  varchar(100))`, `Markdown` (`text`; it holds the validated AI notes as JSON, not Markdown, and the column keeps its old name), `CreatedAt`, `ExpiresAt`,
  `HitCount`. Public page-derived text only: no page HTML and no user data, so one entry serves any
  user. Expired rows are ignored and overwritten on the next store (no purge job yet).
- `LlmUsage`: `Id`, `UserId` (FK to `AppUsers`, cascade delete), `ScanId` (nullable, no FK; usually
  null because the usage is recorded before the scan row exists), `Model` (max 100),
  `PromptTokens`, `CompletionTokens` (the provider's real counts, 0 for a cache hit), `CacheHit`,
  `CreatedAt`. Index on `(UserId, CreatedAt)`. Token counts and model name only.

All timestamps are UTC: always write `DateTime.UtcNow`.

### Clearing history is a soft delete

`DELETE /api/scans` does not remove rows. It sets `DeletedAt` to the current UTC time on the
caller's scans that are not cleared yet (a scan that was already cleared keeps its original
`DeletedAt`). The monthly allowance is computed by counting `Scans` rows, so a hard delete would
refund it and "create, clear, repeat" would get around the limit (only the rate limit would slow it
down). With a soft delete, a cleared scan still counts for the UTC month it was created in.

- `AppDbContext` has a global query filter on `Scan` (`DeletedAt == null`), so the list
  (`GET /api/scans`), get by id (`GET /api/scans/{id}`, a cleared scan is a `404`), and any other
  query never see cleared scans.
- `UsageService` is the only reader that calls `IgnoreQueryFilters()`, so `usage.used` (and the
  `429` quota check in `ScanRecorder`, which runs inside the row-locked transaction) includes
  cleared scans created in the current month. Keep it that way: do not add a `DeletedAt` condition to
  the usage count, and use `IgnoreQueryFilters()` only where cleared rows are meant to be seen.
- Cleared rows are kept (they hold the scan's Markdown). Removing them for good, for example for
  account deletion or a retention job, is a separate task: deleting the `AppUsers` row cascades to
  all of its `Scans` rows, cleared ones included.

## API contract

JSON is camelCase. A scan is `{ id, url, host, createdAt, durationMs, confidence, pages,
tech: [{ name, highlighted }], markdown }` and matches `frontend/lib/types.ts`. Errors are
ProblemDetails JSON. `POST /api/scans` errors carry a `reason`: `"url"` (400, bad URL), `"quota"`
(429, monthly allowance), `"rate"` (429, too fast), `"extract"` (400 when the address is not a
public HTML page, 502 when the site or the page renderer failed; the `detail` is a safe message)
and `"budget"` (429, the daily AI budget is used up and `Ai:FallbackOnBudget` is `false`; has
`resetsAt`, the next midnight UTC).

## Scanner modes and AI credits

`Scanner:Mode` chooses how `POST /api/scans` produces the document:

| Mode | What happens |
|---|---|
| `static` (default) | The MOCK generator. No network, no AI. |
| `extract` | Real fetch (Playwright), reduce and tech fingerprint, then the Markdown is built **without any AI** by `MarkdownDocumentBuilder` (explanation cells show `-`). |
| `ai` | Everything in `extract`, plus one AI call that writes only short explanations as a small JSON object (overview, architecture bullets, a purpose per API endpoint id, design notes, developer tips). If the AI is not configured, over budget or fails, the scan still succeeds with the `extract` document and a footer starting with "Basic scan". |

The real client is `GroqLlmClient` (Groq's OpenAI-compatible `POST {Ai:BaseUrl}/chat/completions`).
It is registered only when `Scanner:Mode` is `ai` AND `Ai:ApiKey` is non-empty; otherwise
`NotConfiguredLlmClient` stays, which makes no network call, so `ai` mode starts fine (one warning
is logged), behaves like `extract` and ends the document with "Basic scan: AI writing is not
configured." Nothing in this repo holds a key.

**Set it up.** Copy `.env.example` to `.env` (ignored by git) and fill in, with your own key:

```
Scanner__Mode=ai
Ai__ApiKey=YOUR_GROQ_API_KEY
Ai__FastModel=openai/gpt-oss-20b
```

`Ai:FastModel` is the model id sent to Groq; it is required when a key is set (startup stops with a
message naming `Ai:FastModel` otherwise). The client sends `max_completion_tokens` (the answer cap
plus `Ai:ReasoningTokenHeadroom`), `temperature` 0, `reasoning_effort` (default `low`) and
`include_reasoning: false` (no reasoning text comes back). A cut-off (`finish_reason: length`) or
empty answer is treated as a failed call: the basic document is used and the real tokens are still
recorded. 401/403 give the safe message "The AI service rejected the server's credentials"; 429/5xx
are retried once. The key, prompts and response bodies are never logged (only status, model, token
counts and latency). To verify, run in `ai` mode, scan a site, and check the document has no "Basic
scan" footer and `LlmUsage` has a row with real token counts; a second scan of the same page is a
cache hit costing zero. An opt-in live test (`PROJECTMD_LIVE_GROQ=1` plus `Ai__ApiKey` in the
environment) makes one tiny real call.

How credits are saved, in order of effect:

1. **Free work stays free.** Tech table, API endpoints, response shapes, colours, fonts, CSS
   variables, outline and meta come from deterministic code and are rendered by
   `MarkdownDocumentBuilder`. The AI only writes short explanations; endpoints are sent as ids
   (`E1 GET /api/users/{id} -> {id:number}`) and answered as `{"id":"E1","purpose":"..."}`, which
   saves output tokens and makes an invented endpoint impossible (unknown ids are dropped).
2. **Small ranked prompt.** `PromptBuilder` sends title and description, tech names, rendering hints,
   API endpoints (first 15), third-party categories, a design summary, the outline and a short text
   excerpt, in that priority, stopping at `Ai:MaxInputTokens` (estimated at about 4
   ASCII characters per token, each other character counted as one token, so it over-counts). No
   raw HTML, scripts, CSS, screenshots or full link list. The facts sit between `<<<FACTS` and
   `FACTS>>>` markers that page text cannot forge.
3. **One call per scan.** No chunk-and-summarise. A too-large page is truncated by priority.
4. **Capped output.** `Ai:MaxOutputTokens` (default 800) is the hard cap; the JSON schema limits
   every field (overview 400 characters, up to 6/5/5 bullets of 140/120/140 characters, purposes
   100 characters); temperature 0.
5. **Cache.** Identical page content (`ContentHash`) + `PromptBuilder.PromptVersion` + model is
   served from `ExtractionCache` for `Ai:CacheDays` for any user: zero credits, recorded in
   `LlmUsage` as a cache hit. The cache stores the validated AI JSON; the Markdown is rebuilt from
   fresh facts every time. Bump `PromptVersion` (now 2) whenever the prompt or schema changes.
6. **Cheap model first.** `Ai:FastModel` is used for everything; `Ai:QualityModel` is reserved. Model
   names are configuration only; with a real client and no `Ai:FastModel` the app stops at startup
   with a clear message.
7. **Stable prefix.** The instructions are one fixed system message and the page facts come last, with
   no user names, ids, timestamps or URL, so provider-side prompt caching can apply.
8. **Hard daily caps.** Before every call `ILlmBudgetGuard` checks the current UTC day's tokens in
   `LlmUsage` against `Ai:DailyTokenCapPerUser` and `Ai:DailyTokenCapGlobal`, reserving the worst
   case (the full prompt plus `Ai:MaxOutputTokens`). Cache hits cost 0. Several requests at the very
   same instant can overshoot a cap by a few calls; the per-user scan rate limit keeps that small.
   A provider 429/5xx gets at most one retry after `Ai:RetryBackoffMs`, never a loop.
9. **No payment for failures.** A failed extraction makes no AI call. A failed AI call is not retried
   automatically (beyond the one transient retry) and the scan falls back to the basic document.

### Structured output and the document

The request uses Groq structured outputs (`response_format` = `json_schema`, `strict: true`, schema
name `scan_notes`; `openai/gpt-oss-20b` supports strict mode, see
https://console.groq.com/docs/structured-outputs), so the answer is constrained to the schema in
`AiDocumentSchema`. Groq does not document `maxLength`/`maxItems` for strict mode, so the limits are
stated in the prompt and descriptions and enforced again by `AiDocumentParser` (cut, never failed).

The AI answer is untrusted (it is built from untrusted page text): wrong shapes are rejected,
`AiProseSanitizer` strips HTML, images and link targets and flattens newlines, unknown or repeated
endpoint ids are dropped, and `MarkdownText` escapes every string (headings, lists, table pipes,
backticks, brackets, `<`/`>`, `://`, `www.`, `@`) when it is written into the document. Invalid
JSON, a refusal, an empty or cut-off answer gives the basic document (no retry; tokens still recorded).

The document layout is fixed: YAML front matter, `# host`, an italic summary line, Overview, Tech
stack, Architecture, API endpoints (table, response shapes, third-party services), Design system
(colours, typography, radii/shadows/spacing, CSS variables, breakpoints, notes), Page structure,
Developer tips and a footer. Sections whose data was not captured are left out. Endpoint ids are
assigned in a fixed order (first-party first, then path, method, host) that does not depend on request
counts, because the cache key ignores counts.

The `Confidence` number is a heuristic (how much usable information the page gave us), not a measured
accuracy, and the document says so. `Pages` is 1 because only the one page is fetched.

## Logging

Request logging records method, path (no query string), status code and duration only. Headers
are never logged, so the `Authorization` header cannot leak into logs.

## Authentication and roles

Every `/api/*` route except `/api/health` needs `Authorization: Bearer <Firebase ID token>`.
The token is validated with no service account: issuer `https://securetoken.google.com/<Firebase:ProjectId>`,
audience `<Firebase:ProjectId>`, RS256 only, lifetime checked with at most 60 s of clock skew.
Firebase's public keys are downloaded from the issuer's OIDC metadata. A missing or invalid token
is `401`; a valid token that lacks the permission is `403` ProblemDetails. For `scan:create` the
403 body has a `reason` field: `"unverified"` (verify the email) or `"role"` (the role cannot do
it). The same `reason` is sent for every refused permission. Tokens are never logged.

The role is stored in the database (`AppUsers.Role`) and decided by the server. A `role` claim in the
Firebase token is IGNORED, so a user can never raise their own role. A stored value that is not
`viewer`, `member` or `admin` is treated as `viewer` (fail closed). The matrix is the same as
`frontend/app/functions/rbac`:

| Role | Permissions (policy names) |
|---|---|
| viewer | `scan:read`, `scan:export` |
| member | viewer + `scan:create`, `history:clear` |
| admin | member + `admin:access` |

`scan:create` also needs `email_verified == true` for every role. Controllers use
`[Authorize(Policy = Permissions.ScanCreate)]` (constants in `Auth/Permissions.cs`) and get the
caller from `ICurrentUser`; `IUserSync.GetOrCreateAsync` returns the caller's `AppUsers` row.
`GET /api/me` returns `{ uid, email, emailVerified, role, usage }`.

### Roles in the database

| Key | Default | Meaning |
|---|---|---|
| `Auth:AdminEmails` | `[]` | Bootstrap admins (string array). Set it as a secret, never in a tracked file |
| `Auth:DefaultRole` | `member` | Role of new users. Only `viewer` or `member` are accepted; anything else is `member` |

```powershell
dotnet user-secrets set "Auth:AdminEmails:0" "<admin email>" --project ProjectMD.Api
```

Elsewhere use the environment variable `Auth__AdminEmails__0` (and `__1`, ...).

- **Bootstrap.** A request whose token has `email_verified == true` and an email on
  `Auth:AdminEmails` (case-insensitive, trimmed) promotes that user to `admin` in the database.
  An unverified email is never promoted. It is promotion only: removing an address does not demote
  anyone, but note that a listed, verified person who is demoted on the admin screen is promoted
  again the next time their role is looked up (within about 30 seconds). Remove the address from the
  list as well to demote them for good.
- **Per request.** `DbRoleClaimsTransformation` runs after the token is validated, finds or creates
  the `AppUsers` row and sets the single `role` claim from the database. The role is cached per
  uid for 30 seconds (`IMemoryCache`, in this server process only) and evicted when an admin
  changes it. `LastSeenAt` is written at most once a minute per user. If the database cannot be read
  the request is `503` ProblemDetails; no default role is ever assumed.
- **Admin endpoints** (need `admin:access`, so `403` with `reason: "role"` for others):
  - `GET /api/admin/users`: up to 200 users, newest first: `[{ id, email, role, createdAt, lastSeenAt }]`.
  - `PUT /api/admin/users/{id}/role` with `{ "role": "viewer|member|admin" }` returns the updated
    user. Errors are ProblemDetails with a `reason`: unknown role `400` `role`; your own account
    `400` `self`; removing the last admin `409` `last-admin`; unknown user `404`. The last-admin
    check and the update run in one serializable transaction (and one at a time per server).
    Role changes are logged at Information level with ids only (no emails).
- Because the cache is per process, with several server instances a role change can take up to 30
  seconds to reach the other instances.

## Page extraction

The free front half of a scan (used when `Scanner:Mode` is `extract` or `ai`; no AI, no credits):
`IExtractionService.ExtractAsync(url)` renders the page in headless Chromium (Playwright for .NET),
`HtmlReducer` turns it into a small `PageDigest` (title, description, outline, main text, links,
JSON-LD, script/stylesheet addresses, a few headers, a content hash; typically over 90% fewer
characters than the HTML), and `TechFingerprinter` detects technologies from `Services/Extraction/TechRules.json`
(about 45 rules; add a technology by adding data, not code). Code is in `ProjectMD.Api/Services/Extraction`.

### Install the browser (once per machine; the app never downloads it)

Build first, then install Chromium with the script that the build copies next to the app:

```powershell
cd backend
dotnet build
pwsh ProjectMD.Api/bin/Debug/net10.0/playwright.ps1 install chromium
```

On Linux add `--with-deps` (needs root) and use the `bin/Release/...` path for a release build. If
`pwsh` is not installed, use the Playwright CLI instead: `dotnet tool install --global
Microsoft.Playwright.CLI` then `playwright install chromium`. Alternatively, set
`Scanner:BrowserChannel` to `chrome` or `msedge` to use a browser that is already installed.
Without a browser, a scan fails with the message "The page renderer is not installed on the server"
(HTTP 502); nothing else breaks.

### Settings (section `Scanner`, all optional)

| Key | Default | Meaning |
|---|---|---|
| `MaxConcurrentFetches` | `2` | Pages rendered at the same time (others wait up to `QueueWaitSeconds`, default 30) |
| `MaxHtmlBytes` | `2097152` | HTML kept per page; more is cut off, not rejected |
| `MaxResourceBytes` / `MaxTotalBytes` | `5242880` / `26214400` | Largest single script/style/data file, and total bytes one page may load |
| `NavigationTimeoutSeconds` / `OverallTimeoutSeconds` | `30` / `45` | Wait for the page to settle, and the hard limit for the whole fetch |
| `RequestTimeoutSeconds` | `15` | Limit for one network request |
| `MaxRedirects` / `MaxRequestsPerPage` | `8` / `300` | Redirect hops per request, and requests per page |
| `RejectHttpErrors` | `true` | A site answering HTTP 400 or higher is an error, not a page to document |
| `BrowserChannel` | empty | `chrome` or `msedge` to use an installed browser instead of Playwright's Chromium |
| `MaxTextChars` / `MaxLinks` / `MaxHeadings` | `12000` / `40` / `60` | Size of the reduced page |

### How requests are kept away from private networks (SSRF)

Intercepting the browser's requests is not enough on its own: Playwright does not show the redirect
hops of an intercepted request to the handler, so `public site -> 302 -> http://169.254.169.254/`
would get through. So the browser is started with name resolution switched off (it cannot connect to
anything by itself, not even an IP address) and every request is answered by our own code:
`RequestGuard` allows only http/https, ports that are not mail/database/admin ports, and hosts whose
DNS records are ALL public (`UrlValidator.IsPublicAddress`); `GuardedHttpClient` then connects only
to the addresses that were checked (no DNS rebinding), follows redirects one hop at a time with a
check on each hop, and enforces the size and time limits. Service workers are blocked, WebSockets
are closed, popups, downloads and dialogs are refused, and images, fonts and media are not fetched.
Errors reach the user as `ExtractionException` with a safe message (no URL, address or stack trace).

Known limits: `robots.txt` is not read yet (`TODO(robots)`); the guard cannot replace network egress
rules, so also deny the server access to private ranges and the cloud metadata address at the
firewall; some sites block headless browsers, which shows up as an HTTP error or a thin page.

### Design-system capture

After the page has loaded, `DesignCapture` (`Services/Extraction/Design/`) runs one read-only script in the
page (5 second cap) that samples up to 800 visible elements with `getComputedStyle`. It never reads text,
cookies, storage or form values and makes no requests. The raw counts are turned into `RawPage.Design`
(a `DesignSystem`, normally under 1.5 KB of JSON; extra CSS variables are dropped above that, never below 20):
the top 12 colours as `#rrggbb` (shades within about 4 per channel are merged) with a guessed role
(`text`, `background`, `accent`, `border`), up to 4 font families with weights and sizes, 5 radii, 4 shadows,
the 8 most used spacing steps, up to 40 `:root`/`html` CSS variables, up to 8 `@media` pixel breakpoints, and
`"Tailwind"` when more than 40% of the sampled elements carry 3+ Tailwind-style classes. Variables and
breakpoints come from the stylesheet text that the request proxy already fetched (each sheet cut to 256 KB,
parsed, then discarded) plus `<style>` blocks of the rendered page. The order is fixed (most used first, ties by
value), so the content hash stays stable. Any failure gives `Design = null`; a scan never fails because of it.
Values are logged only at Debug level, and not at all by default.

### API capture

While the page loads, `PageRequestProxy` (which already performs every request through the SSRF-guarded
client) tells an `ApiRecorder` about each answered request; no new network path is added. The result is
`RawPage.Api` / `PageDigest.Api` (an `ApiCatalog`), built in `Services/Extraction/Api`:

- First-party `xhr`/`fetch` calls (same registrable domain as the page, simple last-two-labels rule plus
  a list of suffixes such as `co.uk`, `co.za`) are grouped by method + host + path template. Numbers,
  UUIDs, hex and token-looking segments become `{id}` (`/api/users/42` -> `/api/users/{id}`).
- Each endpoint keeps: count, distinct statuses, media type, sorted query parameter **names** (max 10), the
  JSON response layout (types only, depth 3, 20 keys, first array element, only for JSON up to 64 KB) and,
  for GraphQL (POST to a `graphql` path, or a JSON body with a GraphQL `query`), one endpoint per operation
  such as `query GetUser` or `anonymous query` (batched bodies give one each).
- Other hosts are summarised as `ThirdPartyHost` (request count + category: analytics, ads, fonts, cdn,
  payments, auth, chat, monitoring, maps, video, or unknown) and are not listed as endpoints.
- Limits: 40 first-party endpoints (busiest first, then alphabetical; `Truncated` is set when more
  exist). Any capture error makes `Api` null; the scan carries on.
- Never stored: request or response bodies, header values, cookies, query-string values, URL fragments,
  user info, GraphQL variables or query text, any value from a JSON response. Nothing captured is logged.
- `PageDigest.ContentHash` includes a canonical, order-independent list of endpoints (method, template,
  first-party flag, GraphQL operation) and the design system, but not counts, statuses or response
  shapes, so the cache only invalidates when the API surface or the design really changes (`CaptureHash`).

### Tests

`dotnet test` runs the extraction tests with fixture HTML, a fake DNS resolver and a local test web
server (no external network, no browser). Tests that drive a real browser are skipped unless
`PROJECTMD_BROWSER_TESTS=1` (and a browser is installed; set `PROJECTMD_BROWSER_CHANNEL=chrome` to use
an installed Chrome). One of them fetches `https://example.com`.
