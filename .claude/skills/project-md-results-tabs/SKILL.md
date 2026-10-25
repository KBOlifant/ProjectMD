---
name: project-md-results-tabs
description: Use when adding, removing, reordering, or styling the tabs on the results page.
---

# Results Tabs

Defined in `frontend/components/results/results-view.tsx`.

## Tab contract

Three things must stay in step: the `Tab` union, the `TABS` order array, and `TAB_LABELS`, plus the matching `tab === "..."` branch in the panel. Also update the `tabRefs` initial object.

Current tabs, in order: `preview`, `design`, `components`, `animations`, `raw`.

## Behaviour

- Tab state is local to `ResultsView`. The route does not change.
- The active tab has a `mulberry-500` bottom border, and `aria-selected`, so colour is not the only cue.
- Each tab body decides if it has data (`hasDesignData`, `hasComponentData`, `hasAnimationData`). When it does not, show a short note that says why (old scan, scanned in static mode, AI mode off). Never show a blank panel.

## Accessibility (already implemented, keep it)

- `role="tablist"`, `role="tab"`, `role="tabpanel"`, `aria-selected`, `aria-controls`, `aria-labelledby`.
- Roving tabindex: only the selected tab has `tabIndex=0`.
- Left and right arrows move between tabs, wrapping at the ends.
- One shared panel id is used for all tabs. Keep that consistent if you add a tab.

## Adding a tab

1. Add the id to `Tab`, `TABS`, `TAB_LABELS`, and `tabRefs`.
2. Write the panel in its own file in `components/results/`, with a `has...Data` guard.
3. Add the branch to the panel, with a fallback note.
4. Add the data to `ScanAnalysis` in `lib/types.ts` and the backend (`../project-md-scan-history/SKILL.md`).

## Verification

- Activate every tab with the mouse and with arrow keys.
- Open an old scan and confirm every tab explains itself.
- Check that labels do not wrap and the tab row scrolls on 375px.
