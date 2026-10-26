---
name: screen-results
description: Build the Project MD Results screen (wireframes v2): two-pane output view with sidebar and Preview, Raw, Design, Components and Animations tabs.
---

# Screen: Results (`/results`, `/results/[id]`)

**Owns:** `frontend/app/(workspace)/results/*`, `frontend/components/results/*`.

## Read first
Wireframes v2: "Results preview", "Design", "Components", "Animations". Also 26-design-tab.md for the data contract.

## Layout (desktop: full screen height, panes scroll inside; mobile: stacked)
- **Top bar:** `OUTPUT` label, divider, host, `.md/.txt` select, then **Copy** (secondary) and **Download** (primary) at the right. Both disabled with an explanation when the account lacks `scan:export`.
- **Left pane (270px, `ResultsSidebar`):** an illustrative thumbnail (clearly not a screenshot), **Tech fingerprint** (2-column `TechChip` grid, first chip highlighted), **Scan details** (Pages crawled, Scan time, Technologies, Confidence). Only show fields the scan really has.
- **Right pane:** tab bar Preview, Raw, Design, Components, Animations (icons on the last three, `violet-500` 2px underline, ARIA tabs with arrow-key navigation). Panel scrolls; Preview/Raw are 760px wide, the rest up to 1024px.

## Tabs
- **Preview:** `MarkdownPreview` of the document without its design sections (h1 `ts-h1`, h2 with a hairline, plum bullets, dark code blocks).
- **Raw:** the whole Markdown in a `<pre>`.
- **Design:** `TabHeader` "Captured design system / Design tokens" with a token count, then cards: Colours, Fonts, Tokens, Export design system (see 26).
- **Components:** header "Reusable UI / Captured components"; numbered cards with a sandboxed iframe preview (light/dark background toggle), code, and Copy HTML / CSS / code.
- **Animations:** header "Motion inventory / Captured animations"; a global **Framer Motion | GSAP** toggle (Framer default); three stat cells (Animations, Measured on the page, Libraries found); numbered cards with a "Copy Framer" / "Copy GSAP" button.
- A tab with no data shows one plain sentence explaining why (older scan, or AI mode needed). Never fake data.

## Rules
- Previews render in `sandbox` iframes; no raw HTML from scans in the page DOM.
- No data is stored here; tab, format and copy state are local.

## Done when
All five tabs render from a real scan and from the static mock; copy and download work; keyboard can reach every control.
