---
name: screen-history
description: Build the Project MD History screen listing every past scan.
---

# Screen: History (`/history`)

**Owns:** `frontend/app/history/*`.

## Read first
- Artifact `components/` (`HistoryRow`'s parts: `TechChip`, badge) and README "Content fundamentals" for the empty state.

## Build
- `ts-h1` "Scan History" with the count in `ts-small`, and a ghost "Clear history" button that asks to confirm before wiping.
- `HistoryRow` per scan, newest first, each linking to `/results/<id>`; `space-3` between rows.
- Empty: `EmptyState` "No scans yet. Generate your first .md file to see it here." with a link to `/`.

## Rules
- Clear history needs a two-step confirm (button turns to "Confirm clear", `danger` text), never a single click.
- Wait for `useIsHydrated` before rendering the empty state.
- Rows are real links, not clickable divs.

## Done when
Scans appear after completing them, survive reload, open their results, and clearing works with confirmation.
