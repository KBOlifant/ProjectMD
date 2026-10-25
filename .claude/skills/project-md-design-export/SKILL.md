---
name: project-md-design-export
description: Use when changing the Figma tokens, CSS variables, or Tailwind config export from the Design tab.
---

# Design Export

Pure builders: `frontend/lib/design-export.ts`. UI: `frontend/components/results/design-export.tsx`. Check script: `frontend/lib/design-export.check.ts`.

## Formats (`EXPORT_FORMATS`)

| id | file | content |
| --- | --- | --- |
| `figma` | `design-tokens.json` | Tokens Studio JSON: colours, font families, border radius, spacing, box shadows. Import with the free Tokens Studio plugin. |
| `css` | `design-tokens.css` | A `:root` block of `--color-*`, `--font-*`, `--radius-*`, `--space-*`, `--shadow-*`. |
| `tailwind` | `tailwind.config.js` | Tailwind v3 config with everything under `theme.extend`. |

The app itself uses Tailwind 4. The exported config is v3 on purpose; do not confuse the two.

## Safety rules (the values come from a page we do not control)

- Colours must be real hex codes (`cleanColours`). Lengths must match `safeLength` (`8px`, `0.5rem`). Shadows must pass `safeShadow` (plain characters, at most 200). Font names must match `^[A-Za-z0-9 _.-]+$` and be at most 60 characters.
- Anything that fails is left out, never written into the file.
- Multi-layer or inset shadows are kept in CSS and Tailwind, and skipped in Figma, because Tokens Studio takes one layer per token (`parseShadow` returns null).
- Colour keys are named by role (`text-1`, `accent-2`) via `slug`.

## UI

- Three cards, each with Copy and Download. Text is built on click from the scan.
- Both buttons need `scan:export`. When denied, they are disabled with a `title` and a visible warning.
- Copy feedback uses the shared `useCopy` message.

## Adding a format

1. Add a builder `(design) => string` in `design-export.ts` that goes through `collect()`.
2. Add an `EXPORT_FORMATS` entry with `filename` and `mime`.
3. Extend `design-export.check.ts`.

## Verification

- Run `npx tsx lib/design-export.check.ts` in `frontend/`. It runs the Tailwind output as real JavaScript and confirms an odd font name and an injected value stay out.
- Open the downloaded JSON, CSS and JS and parse them.
- Not yet verified: importing the JSON into Figma with Tokens Studio. Do that by hand before claiming it works.
