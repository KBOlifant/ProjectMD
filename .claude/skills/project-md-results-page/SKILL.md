---
name: project-md-results-page
description: Use when changing `/results` and `/results/[id]`: the header, export toolbar, tech chips, or the five output tabs.
---

# Results Page

Routes: `app/(workspace)/results/page.tsx` (newest scan) and `app/(workspace)/results/[id]/page.tsx` (that scan; `params` is a Promise in this Next version, so `await` it). Both render `components/results/results-view.tsx` (`ResultsView`).

## Structure, top to bottom

1. Header: initials tile, `<host> Documentation`, URL, scan date, duration, page count, `ConfidenceBadge`
2. Toolbar: format select (`.md` or `.txt`), Copy, Download
3. Tech fingerprint chips
4. Tabs, then one panel

## Tabs

Order: Preview, Design, Components, Animations, Raw. Each panel has its own file; read the matching feature skill before changing one.

- Preview and Raw: `../project-md-markdown-output/SKILL.md`
- Design: `../project-md-design-tokens/SKILL.md` and `../project-md-design-export/SKILL.md`
- Components: `../project-md-component-snippets/SKILL.md`
- Animations: `../project-md-animation-snippets/SKILL.md`
- Tab contract: `../project-md-results-tabs/SKILL.md`
- Chips: `../project-md-tech-fingerprint/SKILL.md`
- Copy and download: `../project-md-copy-download/SKILL.md`

## Rules

- The scan is found in the history store by `id` (or `history[0]`). Return `null` until `useIsHydrated()`, then show `EmptyState` with an "Open History" link if it is missing. Use `loadError` first if there is one.
- Preview shows the Markdown without the design sections. Design shows `analysis.design`; if only old text exists it falls back to the Markdown design sections with a notice. Raw and the Copy/Download buttons use the whole Markdown.
- Copy and Download need `scan:export`. When denied, disable them with a `title` and visible text: "Your account cannot export scans."
- Every panel handles missing data with a short note. Old scans have no `analysis`.
- Only the tab panel changes. Tab, format and copy state are local and never stored.
- A wide code block must scroll inside its own box, not widen the page.

## Verification

- Open a new scan and an old one (no `analysis`); every tab must render or explain itself.
- Open a bad id: the empty state appears.
- Check the export buttons with and without the `scan:export` permission.
