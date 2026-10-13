---
name: data-provider
description: Own the scan-provider seam: static mock data now, a backend API later. Also scope scan history per signed-in user.
---

# Data provider (static now, API later)

**Owns:** `frontend/app/functions/data/*`, `frontend/lib/scan.ts`, `frontend/lib/history-store.ts`, `frontend/components/scan-provider.tsx`.
**Supersedes** the "`buildScan` is the seam" note in [03-data-and-state.md](03-data-and-state.md): the seam is now `scanProvider.generate()`.

## Build
```
app/functions/data/
  scan-provider.ts      ScanProvider interface + `scanProvider` (chosen by NEXT_PUBLIC_SCAN_PROVIDER: "static" default | "api")
  static/mock-scan.ts   buildScan(url) moved here from lib/scan.ts, unchanged behaviour
  static/*.ts           any static content buildScan uses (sample markdown, tech lists)
  api-provider.ts       STUB
```
- Interface:
  ```ts
  interface ScanContext { getToken?: () => Promise<string | null> }
  interface ScanProvider { generate(url: string, ctx?: ScanContext): Promise<Scan> }
  ```
- `static` provider wraps `buildScan(url)` in a resolved promise.
- `api-provider.ts` exports the same interface; `generate` throws `new Error("API provider not implemented")`. Header comment: it will call the backend with `Authorization: Bearer <ID token>`; the backend runs the Groq LLM generation and must re-verify the token, role and `email_verified`. **Do NOT implement any API call, Groq code or API-key handling.** Keys live server-side only.
- `lib/scan.ts` keeps `PIPELINE_STEPS`, `TOTAL_MS`, `stepStates` and imports nothing from `functions/data`. Remove `buildScan` from it (screens must not import it any more; grep for users and update them; the only caller should be the provider).
- Naming note, put it in a comment in both files: `components/scan-provider.tsx` is the React context for the in-progress scan; `functions/data/scan-provider.ts` is the data source.

## Provider component changes (`components/scan-provider.tsx`)
- At `TOTAL_MS` call `await scanProvider.generate(url, { getToken: getIdToken })`, then `addScan` and set `scanId`. Guard against the session having changed (cancelled or replaced) while awaiting.
- On rejection set `session.error` (string) and stop the timer. Add `error?: string` to `Session`.
- `startScan(url)` throws `PermissionError` unless `can(user, "scan:create")` (uses `useAuth()`; import from `@/app/functions/rbac/...`). Callers handle it.
- When the signed-in user's `uid` changes (or becomes null), reset the session to `null`.

## History scoping (`lib/history-store.ts`)
- Export `setHistoryOwner(uid: string | null)`. The key becomes `projectmd.history.<uid>`. Changing owner resets the in-memory cache, re-reads that user's key, and notifies subscribers. With `null`, history is empty and nothing is written. Leave any old `projectmd.history` key untouched (do not migrate or delete).
- `useHistory`, `addScan`, `clearHistory`, `useIsHydrated`, `USAGE_LIMIT` keep their signatures. `clearHistory` requires no check here (UI gates it with RBAC).
- Keep every `localStorage` access in try/catch.

## Rules
- No state set synchronously in an effect body.
- Mock content stays labelled as mock in comments.
- Short plain-English comment above each function, matching the repo style.

## Done when
`npm run lint` and `npm run build` pass; a scan still walks through the steps and saves to history; `buildScan` is only referenced by `static/`; two accounts in the same browser see separate histories.
