---
name: project-md-visual-system
description: Use for any Project MD colour, typography, surface, radius, spacing, or global styling change.
---

# Visual System

## Direction

Project MD is a precise dark-mode tool with monospace headings and a restrained plum/mulberry accent. Source of truth: the design system artifact linked in `agent-skills/README.md`; if it disagrees with the code, the artifact wins.

## Where tokens live

`frontend/app/globals.css`: a `:root` block of CSS variables, an `@theme inline` block that exposes them to Tailwind 4, and `@utility ts-*` type styles. Fonts are loaded in `app/layout.tsx` (`--font-inter`, `--font-jetbrains`).

## Colour tokens

- Surfaces: `bg-base #170c1c`, `bg-sidebar #1e1023`, `bg-surface #28162d`, `bg-raised #341d39`
- Borders: `border-subtle #3a2240`, `border-strong #5e3a5c`
- Accent: `fig-700`, `fig-500 #7a4058`, `mulberry-500 #a35b72`, `mulberry-400 #b86d85`, `rose-400 #d3a19a`, `damson #46294f`
- Text: `ink #f5ede1`, `ink-body`, `ink-muted`, `ink-dim`
- Status: `success`, `warning`, `danger`

## Type

- JetBrains Mono: `ts-display`, `ts-h1` to `ts-h3`, `ts-stat`, `ts-label` (uppercase, tracked), `ts-mono-ui`, `ts-code`
- Inter: `ts-body`, `ts-body-strong`, `ts-small`
- Use a `ts-*` class, never ad hoc font sizes. Build hierarchy with size and weight.

## Rules

- Use token classes (`bg-bg-surface`, `text-ink-muted`, `rounded-md`), never raw hex or arbitrary values for colour, radius or type. Add a token in `globals.css` first if one is missing.
- Radii: `sm 4`, `md 8`, `lg 12`, `xl 16`, `pill`.
- Surfaces are flat with a one-pixel border. `shadow-panel` is for floating cards; `shadow-glow` only for focus on the URL box.
- The `hero-glow` utility is the one gradient. Use it on the Generate hero and the auth backdrop only.
- One exception to "no raw colours": the component preview frame uses fixed white and near-black backgrounds because it shows someone else's site, not app UI.
- Focus: the global `:focus-visible` outline (2px `rose-400`) must stay. Do not remove outlines.
- Keep transitions short (under about 220ms). `prefers-reduced-motion` is handled globally in `globals.css`.

## Verification

- Search the diff for `#` colours and `[...]` arbitrary values.
- Check contrast of `ink-dim` text on `bg-surface` before using it for anything essential.
- Run `npm run lint` and `npm run build` in `frontend/`.
