---
name: project-md-history-page
description: Use when changing `/history`: the scan list, the clear-history flow, or load and error states.
---

# History Page

Route: `frontend/app/(workspace)/history/page.tsx`. Data comes from `lib/history-store.ts` (see `../project-md-scan-history/SKILL.md`).

## Owned UI

- Heading "Scan History" and a count line: "N scans saved to your account" (api mode) or "in this browser" (static mode)
- "Clear history" with a two-step confirm (Keep / Confirm clear), inside `<Can permission="history:clear">`
- One `HistoryRow` per scan, linking to `/results/<id>`
- `EmptyState` for no scans, and for a failed load with a Retry button

## Rules

- Read with `useHistory()`, `useIsHydrated()` and `useHistoryStatus()`. Show nothing until hydrated, so "No scans yet" never flashes.
- Load failure with no scans: show the error and `reloadHistory`. Load failure with scans: keep the list and show the error line above it.
- `clearHistory()` is async. In api mode it calls `DELETE /api/scans` first and empties the cache only on success. On failure keep the list and show the message. Track `clearing` so the buttons disable while it runs.
- `confirming`, `clearing`, `clearError` are local state, never stored.
- Render rows from data, one component, no duplicated markup.
- Rows use flat surfaces and neutral borders; no per-row colour.

## Verification

- Static mode: save two scans, clear, reload; the list stays empty.
- Api mode: stop the backend and try Clear; the list stays and an error shows.
- Sign in as a `viewer`: the Clear button is not rendered.
- Check a very long domain and many tech chips at 375px.
