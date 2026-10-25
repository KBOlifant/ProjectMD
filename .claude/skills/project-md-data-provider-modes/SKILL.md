---
name: project-md-data-provider-modes
description: Use when changing how the frontend gets scans (static mock vs backend api), the api client, error messages from the server, or environment variables.
---

# Data Provider Modes

Files: `frontend/app/functions/data/` (`mode.ts`, `scan-provider.ts`, `api-provider.ts`, `api-client.ts`, `me-store.ts`, `usage-store.ts`, `usage.ts`, `admin-api.ts`, `static/*`) and `frontend/components/scan-provider.tsx`.

## Two providers, one switch

`NEXT_PUBLIC_SCAN_PROVIDER` is read once at build or dev-server start (restart `npm run dev` after changing it).

- unset or `static`: `staticScanProvider` returns a deterministic mock scan (`buildScan`). No network. History in `localStorage`.
- `api`: `apiScanProvider` posts `{ url }` to `POST /api/scans` at `NEXT_PUBLIC_API_URL` (default `http://localhost:5080`) with `Authorization: Bearer <Firebase ID token>`.

Anything else falls back to static. The mock is never used in production; do not ship a build with `static` as if it were a real scan.

## Naming trap

`components/scan-provider.tsx` is the React context for the in-progress scan (timer, session, result id). `app/functions/data/scan-provider.ts` is the data source. `lib/scan.ts` holds the step definitions and must not import from `app/functions/data`.

## api-client rules (`apiFetch`)

- Adds the token as a header only, never in a URL. Never logs tokens or bodies.
- On 401 it retries once with a fresh token (`getIdToken(true)`).
- Every failure becomes an `ApiError` with `status` (0 means the server could not be reached), `title`, `detail`, and `reason`.
- `ApiErrorReason` covers every reason the backend sends: `unverified`, `role`, `quota`, `rate`, `budget`, `url`, `extract`, `self`, `last-admin` (the `REASONS` list). A new backend reason goes in both.
- `apiErrorMessage` maps errors once to text that is safe to show: 403 `unverified` and `role`, 429 `quota` and `budget`, other 429, reason `extract` and 400 (the server's own user-facing detail), and "can't reach the server". Other server `detail` text is not shown.

## Shared stores

- `me-store.ts`: one `GET /api/me` per sign-in or refresh. It feeds the role (auth provider) and the usage numbers (usage store). A response for an older request or user is dropped.
- `usage-store.ts` and `useUsage()`: static mode counts saved scans against `USAGE_LIMIT` (10); api mode shows the server's numbers. `refreshUsage()` runs after a scan finishes, fails, or history is cleared.

## Rules

- No API keys, models or provider URLs in the frontend. Only `NEXT_PUBLIC_*` values that are safe to publish (the Firebase web config is one).
- All server access goes through `apiFetch`. Do not call `fetch` for the API from components.
- Update `frontend/.env.example` when adding a variable.

## Verification

- Run once in each mode.
- Api mode: stop the backend and confirm "can't reach the server" appears on scan, history and the notice.
- Force a 403 and a 429 and check each message.
