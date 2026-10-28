---
name: screen-processing
description: Build the Project MD Processing screen (wireframes v2): centred scan card with a numbered 4-step pipeline.
---

# Screen: Processing (`/processing`)

**Owns:** `frontend/app/(workspace)/processing/*`.

## Read first
Wireframe v2 "Processing". Components: `ScanCardFrame`, `ScanCardHeader`, `ProgressCard` (see 02).

## Build
- Page is vertically and horizontally centred (`min-h-screen`, flex), card 520px max.
- Header: tile, host, full URL, status pill Scanning / Complete / Failed.
- Progress: "Analysis progress" and %, 3px plum bar, four numbered steps: **Fetching website**, **Rendering client-side code**, **Fingerprinting design and stack**, **Writing documentation**. Done steps show a check and `<what> in <seconds>`, the current step has a ringed number and live text, queued steps are dim.
- Footer: ghost **Cancel**, primary **View results** (disabled until complete).
- Failure: same frame, status Failed, the error message in `role="alert"`, buttons "Try again" and "Back".
- With no scan in progress, redirect to `/` or show the last result link.

## Rules
- Progress is announced politely (`aria-live="polite"`), not on every tick. Percent comes from real step state, not a fake timer in server mode.

## Done when
A scan walks through all four steps, completes, and View results opens `/results/<id>`; cancel and failure paths work.
