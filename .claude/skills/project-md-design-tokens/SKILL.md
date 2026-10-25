---
name: project-md-design-tokens
description: Use when changing the Design tab display: colours, fonts, radii, shadows, spacing, CSS variables, breakpoints, or the captured design data behind them.
---

# Design Tab (captured design system)

Files: `frontend/components/results/design-panel.tsx`, `design-colours.tsx`, `design-fonts.tsx`, `design-tokens.tsx`, helpers in `lib/design-utils.ts`. Data type: `DesignSystem` in `lib/types.ts` (mirrors the backend record; every field may be missing). Export has its own skill: `../project-md-design-export/SKILL.md`.

## Data

Captured on the server by `Services/Extraction/Design/` (`DesignCapture`, `DesignAnalyzer`, `CssAnalyzer`, `ColorParser`, `UtilityFrameworkDetector`) and saved in `Scan.analysis.design`. Fields: `colors` (hex, uses, role), `fonts` (family, weights, sizes), `radii`, `shadows`, `spacingScale`, `cssVariables`, `breakpoints`, `utilityFramework`.

The AI writes the "Design breakdown" prose; colours, fonts and values are measured, not guessed.

## Sections

1. Export (only if something is exportable)
2. Colours: main colours as large swatches, the rest as small squares; clicking copies the hex
3. Fonts: one card per family with a live sample, weight ladder, size scale, copy buttons
4. Tokens: radii and shadow boxes, spacing bars, CSS variables (with copy-as-`:root`), breakpoints, utility framework

`hasDesignData` decides if the visual tab shows. If it is false but the Markdown has design sections, the results page shows the text view with a notice.

## Rules

- The values come from a site we do not control. Only pass a value into `style` after a check: `safeLength`, `safeShadow`, `isGoogleFontCandidate`, `cleanColours`. Anything odd is skipped, not rendered.
- Fonts: load only through `googleFontsUrl` for families that pass `isGoogleFontCandidate`, and report whether the font really loaded. A font that is not on Google Fonts shows a fallback with a note.
- Pick text colour on a swatch with `readableTextOn` so labels stay readable.
- Every section copes with a missing field.
- Copy feedback goes through `useCopy` into one `aria-live="polite"` region.
- Server-side: secret-looking CSS variables are removed before saving (`ScanAnalysisJson`). The analysis JSON is capped at 64 KB; see `../project-md-scan-history/SKILL.md`.

## Verification

- Open a real scan and an old scan with no design data.
- Click a swatch and a font copy button; read the live region message.
- Try an odd value (for example a font name with symbols, a shadow with `url(`) and confirm it is dropped.
- Run backend `DesignAnalyzerTests`, `DesignColorTests` and `DesignCssTests` after changing capture.
