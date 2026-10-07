---
name: screen-results
description: Build the Project MD Results screen: header with format menu, Copy and Download, tech fingerprint, and a Preview/Raw Markdown viewer.
---

# Screen: Results (`/results`, `/results/[id]`)

**Owns:** `frontend/app/results/*`, `frontend/components/results/*`.

## Read first
- Artifact README ("Components included" lists the compositions this screen needs), `TechChip`, `Button`.
- Next docs for dynamic routes. `params` is a Promise in this version.

## Build
- `/results` shows the newest scan; `/results/[id]` shows that scan. Both render one client `ResultsView`.
- Header: initials tile, `<host> Documentation` in `ts-h2`, URL and scan metadata in `ts-small`, `ConfidenceBadge`. Actions: format menu (`.md`, `.txt`), secondary Copy, primary Download.
- `TECH FINGERPRINT` label with `TechChip`s; at most three highlighted.
- Tabs `Preview` / `Raw` (`role="tablist"`, arrow-key support). Preview renders the Markdown through a small in-repo renderer that builds React elements (headings, lists, bold, inline code, fenced code). Never `dangerouslySetInnerHTML`. Raw is a `bg-surface` code block in `ts-code`.
- Copy writes to the clipboard and shows "Copied" for 1.5s. Download saves `<host>.md` or `.txt` through a Blob.
- Unknown id: `EmptyState` with a link to History.

## Rules
- Wait for `useIsHydrated` before deciding a scan is missing.
- Long lines in Raw scroll horizontally inside the block, not the page.
- Copy failures (clipboard blocked) show an inline message, not a silent fail.

## Done when
Every scan renders in both tabs, Copy and Download produce the exact Markdown shown in Raw, and a bad id degrades cleanly.
