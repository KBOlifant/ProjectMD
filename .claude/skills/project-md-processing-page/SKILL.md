---
name: project-md-processing-page
description: Use when changing `/processing`: the scan progress card, the failed state, cancel, or the hand-off to results.
---

# Processing Page

Route: `frontend/app/(workspace)/processing/page.tsx`. Reads the in-progress scan from `useScan()` (`components/scan-provider.tsx`).

## Purpose

Show what Project MD is doing while a scan runs, then hand over to the results.

## States (decide in this order)

1. **No session** (`session` is null, for example after a refresh): `EmptyState` with a "Go to Generate" link. The session lives in memory only.
2. **Failed** (`session.error`): `FailedCard` with the message in `role="alert"` and "Try again". Try again calls `cancelScan()` and goes to `/`.
3. **Running or complete**: `ProgressCard` with host, URL, steps, percentage, Cancel, and View Results.

`complete` means `session.scanId !== null`. View Results links to `/results/<scanId>` and stays disabled until then.

## Progress is a timed animation, not real progress

`ScanProvider` runs a 100ms timer for `TOTAL_MS` (3 seconds). When it ends, it calls `scanProvider.generate()`. In api mode the real scan can take much longer (the backend allows 75s), so the bar holds at 95% with the last step "current" until the answer arrives, and reaches 100% only when the scan is saved. Do not word the UI as if each step is measured. Wire real stages only through a backend contract (see `../project-md-processing-stepper/SKILL.md`).

## Behaviour

- Cancel clears the session and returns home. The browser then ignores any late result. Whether the server still finishes and saves that scan has not been checked.
- A refused scan (quota, unverified, rate limit) arrives as `session.error` with a message already mapped by `apiErrorMessage`. Show it as given.
- A different signed-in user never inherits a session (the provider drops it when the uid changes).

## Visual rules

- Flat surfaces (`bg-bg-surface`, `border-border-subtle`), the panel shadow, and accent colour only for progress and the active step.
- Keep the card compact and centred. It must fit a short laptop height.

## Verification

- Run a static-mode scan and watch all four steps finish.
- Force a failure (stop the backend in api mode): the failed card appears and Try again works.
- Open `/processing` directly: the empty state appears.
