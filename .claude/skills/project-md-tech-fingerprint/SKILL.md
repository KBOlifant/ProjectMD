---
name: project-md-tech-fingerprint
description: Use when changing technology detection, the rules file, the tech chips, or the confidence badge.
---

# Tech Fingerprint

## Data

`Scan.tech` is `Tech[]`: `{ name: string; highlighted?: boolean }` (`lib/types.ts`). The name is a plain string with an optional version, for example "React 19".

- Real scans: `Services/Extraction/TechFingerprinter.cs` applies the rules in `TechRules.json` (loaded by `TechRuleSet.cs`) to the fetched page. `Pipeline/TechRoles.cs` and `RenderingHints.cs` use the result when writing the document. Add or change a technology in `TechRules.json`, not in code.
- Static mode: `app/functions/data/static/mock-stacks.ts`. `buildScan` highlights the first three.

## Display

- `TechChip` (`components/ui/tech-chip.tsx`): small pill; `highlighted` uses the accent colours. No icons. The old icon mapping no longer exists.
- Results page: all chips in a wrapping row under "Tech fingerprint".
- `HistoryRow` shows the first three chips.
- `ConfidenceBadge`: green at 95 and above, amber below 80, neutral between.

## Confidence

`ConfidenceEstimator` is a heuristic score of how much usable information the page gave (response, title, description, headings, text, structured data, recognised technologies). It is not a measure of accuracy, and the document says so. Do not describe it as accuracy in the UI.

## Rules

- Keep labels short and add the version only when detected.
- Do not put a technology in the list that was not detected.
- Chips are plain text; do not render page-supplied markup.

## Verification

- Run `TechFingerprinterTests` after changing `TechRules.json`.
- Check a long name and many chips: the row wraps and nothing clips.
