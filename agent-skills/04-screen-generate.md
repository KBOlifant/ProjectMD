---
name: screen-generate
description: Build the Project MD Generate screen: hero, URL input, stat strip and recent scans.
---

# Screen: Generate (`/`)

**Owns:** `frontend/app/page.tsx`.

## Read first
- Artifact README: "Content fundamentals", "Visual foundations"; `components/UrlInput`, `StatTile`.

## Build
- Status pill `AI-POWERED · LIVE` (`radius-pill`, `ts-label`).
- Headline in `ts-display`: `Website → Documentation.` then `Intelligently.` on a second line, in `violet-400` at reduced emphasis (the README says `violet-500`, but its own rule bars `violet-500` text on `bg-base`; the rule wins).
- Sub-copy in `body`: what it does in two short sentences.
- `UrlInput`. On submit run `parseUrl`; on error show it in the field; on success call `startScan(url)` then `router.push("/processing")`.
- `StatStrip` of four tiles (Total Scans, Avg Confidence, Pages Indexed, Avg Speed) computed from history; `—` when empty.
- `RECENT SCANS` label, last three `HistoryRow`s, or `EmptyState`: "No scans yet. Generate your first .md file above to see it here."
- `.hero-glow` behind the hero only.

## Rules
- Exactly one primary button on the view.
- Copy is terse, no exclamation marks, no emoji.
- Wait for `useIsHydrated` before showing the empty state, to avoid a flash.

## Done when
Submitting a valid URL lands on `/processing` with the scan running; an invalid URL shows an inline error and stays put.
