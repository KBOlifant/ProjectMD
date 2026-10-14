---
name: backend-scans
description: Build the scans API: create/list/get/clear endpoints, the scan generator interface with a static mock implementation, URL validation, monthly quota and tests.
---

# Backend scans API

**Owns:** `backend/ProjectMD.Api/Controllers/ScansController.cs`, `Services/*`, `backend/ProjectMD.Api.Tests/*` (shared with 15), and the marked services block in `Program.cs`.
**Depends on:** 14 (models, DbContext, DTOs), 15 (`ICurrentUser`, `IUserSync`, `Permissions`, policies).

## Endpoints (`[Authorize]`, JSON contract in [14](14-backend-foundation.md))
| Method and path | Policy | Behaviour |
|---|---|---|
| `GET /api/scans` | `scan:read` | The caller's scans, newest first, as `ScanDto[]`. |
| `GET /api/scans/{id}` | `scan:read` | One scan; 404 if it does not exist OR belongs to someone else (never 403, never leak existence). |
| `POST /api/scans` | `scan:create` | Body `{ url }`. Validate, enforce quota, generate, save, return `201` with the `ScanDto` and a `Location` header. |
| `DELETE /api/scans` | `history:clear` | Delete all of the caller's scans, `204`. |

All queries are filtered by the caller's `AppUser.Id` (resolved via `IUserSync`). Use `AsNoTracking` for reads. Accept a `CancellationToken` everywhere.

## URL validation (`Services/UrlValidator`)
Port `frontend/lib/url.ts` `parseUrl`, then make it stricter because this is the security boundary: add `https://` when no scheme; accept only `http`/`https`; max 2048 chars; no credentials in the URL (`user:pass@`); host must contain a dot; reject `localhost`, `*.local`, `*.internal`, IP literals in loopback, private (10/8, 172.16/12, 192.168/16), link-local (169.254/16, includes cloud metadata), CGNAT (100.64/10), unspecified, and IPv6 equivalents (`::1`, `fc00::/7`, `fe80::/10`, IPv4-mapped forms). Return `400` ProblemDetails with the same user-facing messages the frontend shows. A later scraper must re-check after DNS resolution; leave a `// TODO(ssrf)` comment saying so, and do not fetch anything now.

## Generator seam (`Services/`)
```csharp
public interface IScanGenerator { Task<GeneratedScan> GenerateAsync(Uri url, CancellationToken ct); }
```
- `StaticScanGenerator`: a faithful port of the frontend mock (`frontend/app/functions/data/static/mock-scan.ts`, `mock-markdown.ts`, `mock-stacks.ts`): deterministic from the host, same shape of output. Label it MOCK in comments and in no response text as measured fact.
- Register `IScanGenerator` -> `StaticScanGenerator`. Add a doc comment on the interface: a Groq-backed generator will replace it later and read its API key from server-side configuration only; **do NOT write any Groq/LLM/HTTP-client code or key handling now.**

## Quota (`Services/IUsageService` + implementation)
`GetAsync(userId)` -> `UsageDto { used, limit, resetsAt }` where `used` = the user's scans created in the current UTC calendar month, `limit` = `Usage:MonthlyLimit` (default 10), `resetsAt` = first instant of next month. `POST /api/scans` returns `429` ProblemDetails (`reason: "quota"`) when `used >= limit`. Do the check and insert in one transaction to avoid racing past the limit (serializable or a row-count recheck after insert, your choice; explain it in a comment).

## Abuse limits
Add an ASP.NET Core rate limiter policy for `POST /api/scans` (per-user via uid, e.g. 5 per minute) returning 429. Cap `Markdown` size you store at a sensible maximum (e.g. 1 MB) and fail with 500-safe ProblemDetails if exceeded.

## Tests (`backend/ProjectMD.Api.Tests`, xUnit)
- `UrlValidatorTests`: valid URLs, scheme added, every rejected class above.
- `StaticScanGeneratorTests`: deterministic for the same host, plausible ranges.
- Controller/quota tests using EF Core's SQLite or in-memory provider (not the real database, never connect to any real database from tests): own-data isolation (user A cannot read B's scan -> 404), quota 429, clear only deletes own scans.

## Rules
- Short plain-English comment above each class/method.
- Never log URLs' query strings or tokens at Information level (host only).

## Done when
`dotnet build` and `dotnet test` pass and every endpoint above exists with those policies.
