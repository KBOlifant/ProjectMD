---
name: backend-design-capture
description: Extract a compact design-system summary (colours, fonts, radii, shadows, spacing, CSS variables, breakpoints) from the rendered page with Playwright.
---

# Backend design-system capture

**Owns:** `backend/ProjectMD.Api/Services/Extraction/Design/*` (new folder), one small hook in `PlaywrightPageFetcher.cs` that fills `RawPage.Design`, tests under `ProjectMD.Api.Tests/Extraction/Design*`, README section.
**Does not own:** API capture ([23](23-backend-api-capture.md), which also maps `RawPage.Design` into `PageDigest` and the content hash) or prompts/Markdown ([25](25-backend-structured-output.md)). Agents 23 and 25 run at the same time: before editing a shared file, re-read it, make the smallest change, and expect to retry if it changed.
**Types already exist** in `Services/Extraction/CaptureTypes.cs` (`DesignSystem`, `ColorToken`, `FontToken`, `CssVariable`); do not rename them.

## What to extract
Run one in-page script (`page.EvaluateAsync`, after load, with a 5 s cap; any failure -> `Design = null`, never fail the scan). It samples up to 800 visible elements (skip `display:none`, zero-size, `script/style/svg` internals) and reads `getComputedStyle`:
- **Colours:** `color`, `background-color`, `border-top-color` (only when border width > 0), `fill` of svg roots optional. Normalise every `rgb()/rgba()/hsl()/named` value to lower-case `#rrggbb`; ignore fully transparent and exact duplicates; merge colours that differ by a tiny amount (round channels to the nearest 4) so anti-aliasing shades do not flood the list. Keep the top 12 by usage. Role guess: most common text colour = `text`, most common opaque background = `background`, a saturated colour used on interactive elements (`a`, `button`, `[role=button]`) = `accent`, border colours = `border`.
- **Fonts:** first family of `font-family` (strip quotes and generic fallbacks), distinct weights and sizes per family, top 4 families; sizes as `Npx` rounded to integers, top 6 per family sorted ascending.
- **Radii:** distinct `border-radius` values of buttons, inputs, cards (elements with a background and radius), top 5. **Shadows:** distinct non-`none` `box-shadow` values, top 4 (shortened to 80 chars).
- **Spacing scale:** collect `margin`/`padding`/`gap` pixel values (>0, <=128), keep the most frequent 8 as a sorted list (`4px`, `8px`, ...).
- **CSS variables:** custom properties declared in `:root`/`html` rules. Read stylesheet text the way that works for cross-origin sheets: parse the CSS text of the stylesheets the page loaded (the guarded proxy already fetches them; capture the text there up to 256 KB each, without storing it beyond extraction) with AngleSharp's CSS parser or a careful regex on `:root{...}` blocks. Keep up to 40 variables (name + value, value cut at 60 chars), preferring colour, spacing, radius and font related names.
- **Breakpoints:** distinct `min-width`/`max-width` pixel values from `@media` rules in those stylesheets, sorted, max 8.
- **Utility framework:** if more than 40% of sampled elements have 3+ class names matching a utility pattern, return `"Tailwind"` when classes look like Tailwind (`flex`, `px-4`, `text-sm`, `md:`...), else `null`.
- Output must stay small (aim under 1.5 KB serialised). Deterministic ordering (by usage then value) so the content hash is stable.

## Safety
- The script reads styles only. It must not read cookies, storage, form values, `innerText` or any page data, and must not make network requests. Do not store page text or element content. Run it in the isolated world if Playwright offers it.
- It runs inside the already-locked-down browser; do not loosen any SSRF rule. CSS text captured by the proxy is parsed and discarded.
- Never log colours, fonts or variable values above Debug level.

## Tests
Unit-test the pure parts without a browser: colour normalisation (rgb, rgba, hsl, named, 3-digit hex, transparent), merge/rounding, top-N and ordering, font family cleaning, spacing frequency, `:root` variable parsing (incl. nested `@media`, comments, `!important`, values with `var()`), breakpoint parsing, utility-framework detection from class lists, size cap. The in-page script itself gets an opt-in browser test (`PROJECTMD_BROWSER_TESTS=1`) against a local test page with known styles.

## Rules
Plain-English comment above each class/method. Do NOT connect to the real database, do NOT read or print `backend/.env` or any key, no external services, no secrets in tracked files, do not run git, do not start the server from your shell. If in-place `dotnet build` fails on a lock from a stuck old server, build and test in a scratch copy under the scratchpad directory (exclude bin, obj, .env) and say so. `dotnet build ProjectMD.sln` 0 warnings; `dotnet test ProjectMD.sln` passes.
