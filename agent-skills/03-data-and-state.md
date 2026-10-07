---
name: data-and-state
description: Own the Project MD data layer: scan types, the scan pipeline, history persistence and the provider that drives the Processing screen.
---

# Data and state

**Owns:** `frontend/lib/*` and `frontend/components/scan-provider.tsx`.

## Build
- `lib/types.ts`: `Scan` (`id`, `url`, `host`, `createdAt`, `durationMs`, `confidence`, `tech[]`, `pages`, `markdown`) and `Tech` (`name`, `highlighted`).
- `lib/url.ts`: `parseUrl(input)` returns `{ ok: true, url, host }` or `{ ok: false, error }`. Add `https://` when the scheme is missing; accept only http(s); reject hosts without a dot and private or loopback addresses ("Any public URL").
- `lib/scan.ts`: `PIPELINE_STEPS` (title, active copy, done copy, duration), `TOTAL_MS`, and `buildScan(url)`. **This file is the only seam to the real backend.** The current implementation is a deterministic mock; replace `buildScan` with an API call and keep the signature.
- `lib/history-store.ts`: localStorage-backed store read through `useSyncExternalStore` (server snapshot is empty, so hydration is safe). Exports `useHistory`, `addScan`, `clearHistory`, `useIsHydrated`.
- `components/scan-provider.tsx`: context exposing `session` (`url`, `elapsed`, `scanId`), `startScan(url)`, `cancelScan()`. A 100ms interval advances `elapsed`; at `TOTAL_MS` it builds the scan, saves it and sets `scanId`.

## Rules
- Wrap every `localStorage` access in try/catch; the app must work with storage blocked.
- No state set synchronously in an effect body; set it from the interval callback or an event.
- Mock data must be labelled as mock in code comments, never presented as measured fact in copy.
- Types are exported from one place; screens import them, never redefine them.

## Done when
Starting a scan walks through every step, saves to history, survives a reload, and `buildScan` is the only function that would change for a real backend.
