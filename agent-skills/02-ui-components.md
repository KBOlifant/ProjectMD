---
name: ui-components
description: Build the Project MD primitive components (Button, UrlInput, NavItem, StatTile, TechChip, ProgressCard and friends) from the design system.
---

# UI components

**Owns:** `frontend/components/ui/*`.

## Read first
- Artifact `components/<Name>/README.md` and `preview.html` for each component below. The README is the spec; the preview is the pixel reference.

## Build (one file each, named exports, typed props)
| Component | Spec highlights |
| --- | --- |
| `Button`, `ButtonLink` | primary / secondary / ghost / disabled. 10px 20px padding, `ts-mono-ui`, `rounded-md`. Primary: `violet-500`, `on-primary`, `shadow-glow`, hover `violet-400`. |
| `UrlInput` | 52px field, `://` glyph, `ink-muted` placeholder, `violet-400` outline + glow on focus, hint line, error in `danger` replacing the hint (`role="alert"`). Enter submits. |
| `NavItem` | default / active (2px `violet-500` marker, `violet-700` tint) / count badge. |
| `StatTile`, `StatStrip` | `ts-stat` value, `ts-small` `ink-muted` caption, `border-subtle` hairlines, four per strip. |
| `TechChip`, `ConfidenceBadge` | chip `radius-sm`, 11px mono; highlighted variant; badge green at 95%+, `warning` below 80%. |
| `ProgressCard` | initials tile, status pill, progress track, step list (done / current / queued), Cancel + View Results. Presentational: the consumer owns step state. |
| `HistoryRow` | link row: initials tile, host, URL, relative time, badge, up to three chips, pages. |
| `EmptyState` | dashed `border-strong` frame, one sentence: what is missing, then what to do. |
| `icons.tsx` | 14px single-weight line glyphs, 1.5px stroke, `currentColor`. |

## Rules
- Components take data through props. They never read the scan store.
- Keyboard and screen reader support: real `button` / `a` / `input` elements, visible focus ring, labels on icon-only controls.
- Contrast: text 4.5:1 on its ground in the shipped theme. `ink-dim` is the one known exception, see the README.
- No emoji, no gradients other than `.hero-glow`, no left-border cards (the nav marker is the only left accent).

## Done when
Each component matches its `preview.html` side by side, and lint and build pass.
