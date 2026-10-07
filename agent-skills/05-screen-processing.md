---
name: screen-processing
description: Build the Project MD Processing screen that shows a scan moving through its pipeline steps.
---

# Screen: Processing (`/processing`)

**Owns:** `frontend/app/processing/*`.

## Read first
- Artifact `components/ProgressCard/README.md` and `preview.html`.

## Build
- Centre a `ProgressCard` on `bg-base`, driven by `session` from the provider.
- Derive from `elapsed` and `PIPELINE_STEPS`: each step is done, current or queued; progress is `elapsed / TOTAL_MS`.
- Cancel calls `cancelScan()` and returns to `/`. When every step is done, View Results → links to `/results/<scanId>`; before that it is disabled.
- No active session: `EmptyState` "Nothing is scanning. Paste a URL on Generate to start." with a link to `/`.

## Rules
- Announce progress politely: `aria-live="polite"` on the current step text, and `role="progressbar"` with values on the track.
- Respect `prefers-reduced-motion`: no animated transitions on the fill.
- The card is presentational; this page only maps provider state to props.

## Done when
A scan started on Generate runs to completion on this page and the button unlocks exactly when the last step finishes.
