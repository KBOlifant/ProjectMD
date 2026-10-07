---
name: foundation-tokens
description: Wire the Project MD design tokens, fonts and base styles into the Next.js app. Use before any UI work.
---

# Foundation: tokens, fonts, base styles

**Owns:** `frontend/app/globals.css`, the font setup and `<html>/<body>` classes in `frontend/app/layout.tsx`.

## Read first
- Artifact `project/tokens.json` and `project/README.md` (Visual foundations, Usage rules).

## Build
1. Fonts via `next/font/google`: `Inter` (variable `--font-inter`) and `JetBrains_Mono` (variable `--font-jetbrains`). Apply both variables on `<html>`.
2. In `globals.css`, declare every token from `tokens.json` as a CSS variable on `:root` using the exact token name (`--bg-base`, `--violet-500`, `--ink-muted`, `--shadow-glow` ...).
3. Map them in `@theme inline` so Tailwind generates utilities that carry the token name:
   - colours: `--color-bg-base: var(--bg-base)` gives `bg-bg-base`, `text-ink-muted`, `border-border-subtle`;
   - radius: `rounded-sm|md|lg|xl|pill` = 4 / 8 / 12 / 16 / 999px;
   - shadows: `shadow-glow`, `shadow-panel`;
   - fonts: `font-sans` = Inter, `font-mono` = JetBrains Mono.
4. Add one `@utility` per text style, prefixed `ts-`: `ts-display`, `ts-h1`, `ts-h2`, `ts-h3`, `ts-stat`, `ts-body`, `ts-body-strong`, `ts-small`, `ts-label` (uppercase, tracking 0.14em), `ts-mono-ui`, `ts-code`. Values come straight from `tokens.json` `type.groups`.
5. Base: `body` on `bg-base` with `ink-body` text at the `body` style; a global `:focus-visible` ring of 2px `violet-400`; `.hero-glow`, a radial `violet-glow` from the top right, used once.
6. Dark only. Remove the template's light/dark switch.

## Rules
- Values are exact. Do not round or "improve" colours.
- Never place `violet-500` text on `bg-base`; use `violet-400`.
- `ink-dim` only for hints and disabled items.

## Done when
`npm run build` passes and a scratch page using `bg-bg-surface text-ink ts-h1 rounded-lg` renders correctly.
