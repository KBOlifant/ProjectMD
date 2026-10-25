---
name: project-md-scan-history
description: Use when changing the Scan record, the history store, how scans are saved or loaded, or the analysis JSON stored with a scan.
---

# Scan Records and History Store

Types: `frontend/lib/types.ts`. Store: `frontend/lib/history-store.ts`. Server: `ScansController`, `ScanRecorder`, `ScanAnalysisJson`.

## Scan record

```ts
interface Scan {
  id: string            // a GUID string from the server (static mode: generated)
  url: string
  host: string
  createdAt: string     // ISO date
  durationMs: number
  confidence: number    // 0-100
  pages: number
  tech: Tech[]
  markdown: string
  analysis?: ScanAnalysis | null   // design, components, animations; `api` is stored but not shown
}
```

`ScanAnalysis` has a `version` and unknown keys are ignored. Every part is optional because old scans have none.

## Two modes (`app/functions/data/mode.ts`)

- **static** (default): history in `localStorage` under `projectmd.history.<uid>`, one key per user. Corrupt or blocked storage falls back to an empty list and an in-memory copy.
- **api** (`NEXT_PUBLIC_SCAN_PROVIDER=api`): the backend is the source of truth. `setHistoryOwner(uid)` loads `GET /api/scans`. `addScan` only updates the cache, since the server already saved the scan. `clearHistory` calls `DELETE /api/scans` first.

## Store rules

- Components read through `useHistory()`, `useIsHydrated()` and `useHistoryStatus()`. Never read storage in a component.
- The owner is the signed-in uid. A load response for an old owner or an older request is dropped (a generation counter). Scans added during a load are merged so they do not vanish.
- Do not fake persistence in local state when the server is the source.
- Use stable `id` values as React keys.

## Server side

- A scan is saved only if the user still has monthly allowance (`ScanRecorder`; see `../project-md-usage-quota/SKILL.md`). Deletion is soft.
- `ScanAnalysisJson` removes secret-looking CSS variables, then shrinks the JSON to fit 64 KB (`MaxBytes`); animation data is dropped before design data.
- Adding a field means changing the C# record, the JSON, `lib/types.ts` and a test (`ScanAnalysisTests`).

## Verification

- Static mode: two accounts in one browser never see each other's scans.
- Api mode: sign out and in; the list reloads. Stop the server; the history page shows an error with Retry.
- Open a scan saved before a new field existed; the page still renders.
