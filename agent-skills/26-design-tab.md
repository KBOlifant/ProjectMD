---
name: design-tab
description: Ship the captured design data to the website and show it in a new Design tab with clickable colour swatches, Google Font previews and visual tokens.
---

# Design tab (feature 1 of 3: design, components, animations)

Two agents work in parallel. Do not edit the other side's files.

## Data contract (JSON, camelCase) added to every scan
`Scan.analysis` (optional, `null` for old scans):
```json
{ "version": 1,
  "design": { "colors":[{"hex":"#0f172a","uses":120,"role":"text|background|accent|border"}],
              "fonts":[{"family":"Inter","weights":[400,600],"sizes":["14px","16px"]}],
              "radii":["8px"], "shadows":["0 1px 2px #0003"], "spacingScale":["4px","8px"],
              "cssVariables":[{"name":"--brand","value":"#38bdf8"}], "breakpoints":["640px"], "utilityFramework":"Tailwind" },
  "api": { "endpoints":[...], "thirdParty":[...], "truncated":false } }
```
`design` and `api` are exactly the existing `DesignSystem` / `ApiCatalog` records (`Services/Extraction/CaptureTypes.cs`), serialised camelCase. Later features add `components` and `animations` next to them (leave room: unknown properties must be ignored by both sides).

## Backend agent (owns `backend/ProjectMD.Api/**` except Extraction capture code, and `backend/ProjectMD.Api.Tests/**`)
- New record `ScanAnalysis(int Version, DesignSystem? Design, ApiCatalog? Api)`; `Scan.Analysis` stored as `jsonb` (nullable) with value converter or `ToJson`; new migration `AddScanAnalysis` (generate with the dummy-connection-string recipe in backend/README.md; do NOT apply; never read `.env`).
- `GeneratedScan` carries the analysis; `PipelineScanGenerator` fills it from `PageDigest.Design/Api`; the static generator may attach a small fixed sample design so static mode shows something. Cap serialised size at 64 KB (drop `api.endpoints` shapes first, then endpoints) and never store secrets (the capture already strips values; add a test with canary strings through the whole pipeline).
- `ScanDto` gains `analysis` (nullable). List and get endpoints return it. Soft-deleted scans still hidden. Existing tests must still pass; add tests for round-trip, null for old rows, size cap.
- An opt-in live test `Live_scan_of_public_site_writes_analysis` (skipped unless `PROJECTMD_LIVE_SCAN=1`; reads `Ai__ApiKey`/`Ai__FastModel` from the environment only, uses `PROJECTMD_LIVE_URL` default `https://tailwindcss.com`, Chrome channel from `Scanner__BrowserChannel`, in-process pipeline, no server, writes the resulting Markdown and analysis JSON to the path in `PROJECTMD_LIVE_OUT`). It must never print the key.
- README + keep `dotnet build ProjectMD.sln` at 0 warnings (output is in `backend/artifacts`).

## Frontend agent (owns `frontend/**` Results screen files and types)
- `lib/types.ts`: `Scan.analysis?: ScanAnalysis` with exact types above. Tolerate missing or partial data everywhere.
- Results page: add a **Design** tab beside Preview/Raw (keep the existing ARIA tab pattern and keyboard support). Hide the tab or show "No design data for this scan" when there is none.
- **Colours:** the 3 to 5 main colours as large square swatches (choose by usage, keep one of each role when available, skip near-duplicates); each is a `<button>` that copies its hex (clipboard with a fallback, brief "Copied #hex" `aria-live` message, label contrast auto-chosen). All other colours as small clickable squares below. Show hex, role and use count.
- **Fonts:** for each family render a live sample in that font: "The quick brown fox...", a weight ladder using the weights found, and the size scale. Load fonts from Google Fonts by injecting one `<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=...">` per family (validate the family against `^[A-Za-z0-9 ]{2,40}$` and URL-encode; request only the found weights; `display=swap`); detect failure (`document.fonts.load` or link `onerror`) and then show "Not on Google Fonts, showing a fallback" with the CSS stack. Skip generic families (serif, sans-serif, system-ui...). Copy buttons for the CSS `font-family` line and the Google Fonts `<link>`/`@import` snippet.
- **Tokens:** visual boxes for radii and shadows, bars for the spacing scale, a list of CSS variables (colour values shown as chips) with "Copy all as :root block", breakpoints as chips, and the utility framework label.
- **Layout (wireframes v2):** `TabHeader` ("Captured design system / Design tokens" plus a token-count badge), then `DesignCard` sections in this order: Colours (swatch cards with a label bar), Fonts, Tokens (radii, shadows, spacing, CSS variables, breakpoints), and last **Export design system** (cards for Tokens Studio JSON, CSS variables, Tailwind config, using `downloadText`). Cards are `bg-surface`, `border-subtle`, `rounded-xl`.
- Dark theme token classes only, no new dependencies, no raw HTML rendering, responsive down to 360px, keyboard accessible, `prefers-reduced-motion` respected. Static mode: the mock scans' sample analysis (from the backend contract) is added to the frontend static mock so the tab works without a server.
- Verify with `npm run lint`, `npm run build`, and look at it in a browser (static mode); report what you saw. Stop any dev server you start.

## Rules for both
Plain-English comments. Do not read or print `backend/.env`, `frontend/.env.local` or any key. Do not run git. Do not start the .NET server from your shell. Build backend only with `dotnet build ProjectMD.sln` from `backend/`.
