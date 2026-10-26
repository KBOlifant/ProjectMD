---
name: ui-components
description: Build the Project MD primitive components (Button, UrlInput, NavItem, TechChip, ProgressCard, HistoryRow and friends) to the v2 wireframes.
---

# UI components

**Owns:** `frontend/components/ui/*`, `frontend/lib/download.ts`.

## Read first
- Wireframes v2. The Components tab mock ("Primary button", "Page header", "Compact navbar") is the pixel reference for button and header styling.

## Build (one file each, named exports, typed props)
| Component | Spec highlights |
| --- | --- |
| `Button`, `ButtonLink` | `primary` (plum `#8b4f67`, hover `plum-400`), `accent` (violet, hero only), `secondary` (surface), `ghost` (outline), disabled. 8px 16px padding, `ts-mono-ui`, `rounded-md`. No glow. |
| `UrlInput` | One `xl` frame (`bg-surface`, `border-strong`, 6px padding): `://` glyph, address field, violet **Analyze website →** button. Hint row under it ("Press enter", "Public URLs only", "Usually 30-60 seconds") or the error (`role="alert"`, `danger`) or a `notice`. Placeholder `your-site.com`. |
| `NavItem` | `layout="rail"` icon tile with marker, badge and tooltip; `layout="row"` icon + text for mobile. |
| `StatStrip` | `ts-stat` value, `ts-small` `ink-muted` caption, hairlines, four per strip. |
| `TechChip` | Results side panel tile: 20px glyph box + name, wraps long names, violet border when highlighted. Glyphs are generic shapes picked by name (atom, triangle, wave, disc, user, search, cube), never brand logos. |
| `TechTag`, `ConfidenceBadge` | Dense-list tag (outline, 10px mono). Badge shows just `94%`, green at 95+, amber below 80; the accessible name says "94% confidence". |
| `ProgressCard`, `ScanCardFrame`, `ScanCardHeader` | 520px card. Header: tile, host, URL, status pill (plum dot "Scanning" / "Complete" / "Failed"). Label "Analysis progress" + %, 3px plum bar, **numbered** steps (done = check, current = ringed, queued = dim), Cancel + View results right aligned. |
| `HistoryRow` | Tile, host (stretched link) + confidence badge, up to four tags, date + page count, **.MD download button** above the link. Never nest interactive elements. |
| `HeroBackdrop` | Full-bleed photo layer for the Generate hero (see 04). |
| `EmptyState` | Dashed `border-strong` frame, one sentence: what is missing, then what to do. |
| `icons.tsx` | 16-grid line glyphs, 1.5px stroke, `currentColor`, `size` prop (default 14). Add new ones here, never inline SVG in pages. |
| `lib/download.ts` | `downloadText`, `downloadScan`, `downloadAllScans`. The only place a Blob link is built. |

## Rules
- Components take data through props. They never read the scan store (the page passes `onDownload`).
- Real `button` / `a` / `input` elements, visible focus ring, labels on icon-only controls.
- Contrast: text 4.5:1 on its ground. Small text uses `ink-muted`, `plum-300` or `violet-400`.
- No emoji, no gradients (except the hero backdrop), no left-border cards.

## Done when
Each component matches its wireframe crop, and lint and build pass.
