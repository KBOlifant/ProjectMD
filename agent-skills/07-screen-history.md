---
name: screen-history
description: Build the Project MD History screen (wireframes v2): archive list with confidence, tech tags, per-row .MD download and Export all.
---

# Screen: History (`/history`)

**Owns:** `frontend/app/(workspace)/history/*`, `frontend/lib/download.ts`.

## Read first
Wireframe v2 "History". Component: `HistoryRow` (02).

## Build
- `PageFrame size="list"`, `Eyebrow` "Archive", `h1` "Scan history", caption "N completed scans".
- Actions at the right: **Export all** (one combined `project-md-export.md` via `downloadAllScans`, needs `scan:export`) and **Clear history** (two-step confirm, needs `history:clear`).
- One `HistoryRow` per scan, newest first: host (opens `/results/<id>`), confidence badge, up to four tech tags, date and pages, and a **.MD** button that downloads that scan.
- Empty state: "No scans yet" with a primary link "Analyze a website".
- Loading and load-error states come from the history store; show the error text, not a blank page.

## Rules
- The row is a block with one stretched link; the `.MD` button sits above it. No nested links or buttons.
- Disabled export controls explain why (title text and visible note).

## Done when
Rows open results, `.MD` and Export all download the correct text, clearing history asks twice, and it works at 390px.
