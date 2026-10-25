---
name: project-md-processing-stepper
description: Use when changing the scan stages, step states, progress maths, or the ProgressCard step list.
---

# Processing Stepper

Data and maths: `frontend/lib/scan.ts`. Display: `frontend/components/ui/progress-card.tsx`. Timer: `components/scan-provider.tsx`.

## Stages (`PIPELINE_STEPS`)

| id | title | ms |
| --- | --- | --- |
| `fetch` | Fetching URL | 300 |
| `render` | Rendering HTML (JavaScript) | 600 |
| `fingerprint` | Fingerprinting Tech Stack | 900 |
| `synthesize` | AI Synthesizing Documentation | 1200 |

`TOTAL_MS` is the sum (3000). Each step has `active` and `done` copy shown by state.

## States

`stepStates(elapsed)` returns `"done" | "current" | "queued"` for each step. There is exactly one `current` step while running. Done steps show a check; queued steps show "Queued". No per-step times are shown, because the times would be invented. Add a time field back only when the backend sends real ones.

## Important: this is not real progress

The timer is a fixed animation. After `TOTAL_MS` the provider holds `elapsed` at `TOTAL_MS - 1` (so the last step stays current) until `scanProvider.generate()` resolves. The bar does not reflect fetch, render or AI time. `progressPercent(elapsed, complete)` follows the animation up to `WAITING_MAX_PCT` (95) and shows 100 only once the saved scan id exists. The backend has the real stages (extraction, AI, document build), but sends no progress events.

## If real progress is added

1. Define the stages with the backend first (for example a status field or polling endpoint). Do not guess timings.
2. Keep the stages as data and derive the percentage from completed stages in one place.
3. Keep error and retry states explicit (the Processing page already has a failed card).
4. Do not enable View Results until a saved scan id exists.

## Motion and accessibility

- Only the current marker and status dot may pulse. `prefers-reduced-motion` is handled globally.
- The bar is `role="progressbar"` with `aria-valuenow`; the current step has `aria-live="polite"`.
- No decorative background animation.

## Verification

- Check the four states render correctly at 0%, mid-way, and complete.
- Confirm the percentage never exceeds 100 and the bar does not look finished while the scan is still running.
