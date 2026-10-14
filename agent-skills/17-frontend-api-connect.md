---
name: frontend-api-connect
description: Connect the frontend's data functions (scan provider, history, usage) to the ASP.NET Core backend using the Firebase ID token.
---

# Frontend -> backend connection

**Owns:** `frontend/app/functions/data/*` (except `static/`), `frontend/lib/history-store.ts`, `frontend/components/scan-provider.tsx` (minimal edits), `frontend/components/shell/usage-meter.tsx`, `frontend/.env.example`, the "Planned: replacing the mock" section of `docs/ARCHITECTURE.md`.
**Depends on:** the API contract in [14](14-backend-foundation.md), [15](15-backend-auth.md), [16](16-backend-scans.md) (do not wait for them to be built; code to the contract).

## Switch
`NEXT_PUBLIC_SCAN_PROVIDER` = `static` (default, current behaviour, must keep working) | `api`. `NEXT_PUBLIC_API_URL` (default `http://localhost:5080`). Add both to `.env.example` (commented, with a note). Do not edit `.env.local`.

## Build
- `app/functions/data/api-client.ts`: `apiFetch<T>(path, init?)`. Adds `Authorization: Bearer <getIdToken()>`; on `401` retries once with `getIdToken(true)`; parses ProblemDetails into `class ApiError extends Error { status, reason?, title }`. `reason` comes from the response's `reason` extension (`unverified` | `role` | `quota`). Network failure -> `ApiError` with a friendly "Can't reach the server" message. Never log tokens or bodies.
- `api-provider.ts` (replace the stub): `generate(url)` -> `POST /api/scans { url }` -> `Scan` (shape already equals `lib/types.ts`). Map `ApiError` to messages the UI can show: 403 `unverified` -> "Verify your email to continue."; 403 `role` -> "Your account is read-only."; 429 `quota` -> "You've used this month's scans."; 429 other -> "Too many requests. Try again shortly."; 400 -> the server's `detail`.
- `lib/history-store.ts`: add a remote mode (only when the provider is `api`). `setHistoryOwner(uid)` triggers `GET /api/scans` and fills the cache (empty list while loading; `useIsHydrated` semantics stay correct, no flash of "No scans yet": expose a `loading` state through the existing hydration hook or a new `useHistoryStatus()`). `addScan(scan)` only updates the cache in remote mode (the server already saved it). `clearHistory()` calls `DELETE /api/scans`, then clears the cache; on failure keep the cache and surface the error. Static mode keeps using `localStorage` exactly as today. Guard against races when the user changes during a request.
- `functions/data/usage.ts`: `useUsage(): { used, limit, loading }`. Static mode: history length and `USAGE_LIMIT`. Api mode: `GET /api/me` (`usage`), refreshed after a scan completes or history is cleared. Update `components/shell/usage-meter.tsx` to use it.
- `components/scan-provider.tsx`: only what's needed so a failed `generate` shows the mapped message in `session.error` and a successful one refreshes usage. Do not change the timer/step behaviour.
- Optionally call `GET /api/me` once after sign-in in api mode to upsert the user row (errors are non-fatal and silent).

## Rules
- Never put the Firebase token or any API key in URLs, `localStorage` or logs. Do not add any Groq/LLM key handling; keys live server-side only.
- The static mode must still build and run with no backend.
- Short plain-English comment above each function, matching the repo style. No new dependencies.
- Do not run sign-up/sign-in against the real Firebase project; test the client against a tiny local fake server or with unit-style scripts.

## Done when
`npm run lint` and `npm run build` pass in both modes (`NEXT_PUBLIC_SCAN_PROVIDER=api` set on the command line); with a fake server returning the contract, a scan is created, appears in history after a reload, usage updates, and clear history empties it; with the server down the UI shows the friendly error.
