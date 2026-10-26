---
name: foundation-tokens
description: Wire the Project MD design tokens, fonts and base styles into the Next.js app (wireframes v2: graphite canvas, plum actions, violet accent). Use before any UI work.
---

# Foundation: tokens, fonts, base styles

**Owns:** `frontend/app/globals.css`, the font setup and `<html>/<body>` classes in `frontend/app/layout.tsx`.

## Read first
- The v2 wireframes (`Project MD Web Application*.png`). The Design tab mock (sitescribe.com, "Design tokens") lists the palette, fonts, radii and breakpoints. The values below were sampled from it.
- The old design-system artifact is **v1 (deep violet)** and is out of date until it is refreshed. Where it disagrees with this file, this file wins.

## Tokens (names are unchanged from v1; only values moved)
| Token | Value | Use |
| --- | --- | --- |
| `bg-sidebar` | `#141416` | icon rail, code blocks |
| `bg-base` | `#18181b` | page canvas |
| `bg-surface` | `#202024` | cards, inputs, panels |
| `bg-raised` | `#29292e` | active nav tile, hover, toggles |
| `border-subtle` / `border-strong` | `#2c2b30` / `#3b3a40` | hairlines / controls |
| `violet-700` | `#3a004d` | avatar and brand tiles |
| `violet-500` | `#9e4abb` | accent violet: hero button, active tab bar, nav marker |
| `violet-400` | `#b565d0` | violet used as **text** and the focus ring |
| `plum-500` / `plum-400` / `plum-700` | `#8b4f67` / `#9e6078` / `#5b3344` | every in-app primary button, progress fills / hover / pressed |
| `plum-300` | `#bb8499` | small eyebrow labels and step numbers |
| `ink` / `ink-body` / `ink-muted` / `ink-dim` | `#f0e0e0` / `#d3c6ca` / `#a89ca2` / `#7c7278` | text, strongest to faintest |
| `success` `warning` `danger` | `#4cc38a` `#e0a84a` `#ef6a80` | status only |

Radii: `sm 4`, `md 6`, `lg 10`, `xl 14`, `pill 999` (px). Breakpoints in the wireframe: 480 / 768 / 1200 / 1440.
Shadows: `shadow-panel` for floating cards and menus. `shadow-glow` is a legacy token; do not use it on new work.

## Build
1. Fonts via `next/font/google`: `Inter` (`--font-inter`) and `JetBrains_Mono` (`--font-jetbrains`), both variables on `<html>`.
2. Declare every token as a CSS variable on `:root` with its exact name, then map it in `@theme inline` so Tailwind generates `bg-bg-surface`, `text-ink-muted`, `border-border-strong`, `bg-plum-500`, `rounded-lg` and so on.
3. Text styles are `@utility` classes prefixed `ts-`: `ts-hero` (clamped 56 to 112px, 800 weight, tight tracking, used once), `ts-display`, `ts-h1` (26px), `ts-h2`, `ts-h3` (14px), `ts-stat`, `ts-body`, `ts-body-strong`, `ts-small`, `ts-label` (10px mono, uppercase, 0.14em), `ts-mono-ui` (12px), `ts-code` (12px). Mono carries headings and UI; Inter carries reading text.
4. Base: `body` on `bg-base` with `ink-body`; a global 2px `violet-400` `:focus-visible` ring; reduced-motion rule; `.pane-scroll` thin scrollbars for the Results panes.
5. `hero-glow` survives only for the sign-in screens. The home hero uses a photo (see 04).
6. Dark only.

## Rules
- Actions are **plum** (`variant="primary"`). Violet is for the brand moment: the hero button, the active tab bar, the nav marker, tiles. Never use two primary buttons in one view.
- Small text on dark: use `plum-300` or `violet-400`, never `violet-500` or `plum-500` text. Large display text may use `violet-400`.
- `ink-dim` is for disabled items and decorative marks only. Hints and counts use `ink-muted` (contrast 4.5:1).
- Values are exact. Do not round or "improve" colours.

## Done when
`npm run lint` and `npm run build` pass, and a scratch page using `bg-bg-surface text-ink ts-h1 rounded-lg bg-plum-500` renders correctly.
