---
name: project-md-animation-snippets
description: Use when changing animation capture, the AI motion notes, the GSAP and Framer Motion snippets, or the Animations tab.
---

# Animations

UI: `frontend/components/results/animations-panel.tsx`. Types: `AnimationReport` and `AnimationItem` in `lib/types.ts`. Stored in `Scan.analysis.animations`.

## Pipeline

1. **Capture** (`Services/Extraction/Visual/`): `MotionScript` and `MotionEvidenceReader` read the animation libraries on the page and the animations the browser is running (real durations and easing). `VisualCapturer` takes scroll screenshots; `ContactSheet` lays them out in grids.
2. **AI notes** (`Services/Ai/MotionAnalyzer`, `MotionPromptBuilder`, `MotionNotes`): the vision model describes each animation. Disabled by `Motion:AiEnabled=false`.
3. **Report** (`Services/Pipeline/AnimationReport.cs`): merges measured facts and notes, guesses the library, picks a trigger.
4. **Snippets** (`MotionSpec.cs`, `AnimationSnippets.cs`): fixed templates built from a `MotionSpec`.

All settings are in the `Motion` section of `appsettings.json`; see `../project-md-scanner-motion-settings/SKILL.md`.

## Code safety rule

Snippet code is built only from numbers and whitelisted words (GSAP ease names, trigger words, property names). Text from the page or the AI must never end up inside code. Keep it that way: any new field that goes into a snippet needs a whitelist or a numeric parse. The server only shows the code; it never runs it. `.target` is a placeholder for the reader.

## UI

- Cards show name, section, trigger label (`TRIGGER_LABELS`), "Measured" or "Inferred from screenshots", and "Looks like <library>".
- A GSAP / Framer Motion switch (`aria-pressed`) and a Copy code button. Equivalent snippets must keep the same intent, duration, movement and easing.
- The page says the library is a guess and the code is a starting point.
- `hasAnimationData` guards the tab; old scans and scans without screenshots show a note.

## Rules

- Prefer transform and opacity in generated code. Mention reduced motion when adding a long animation.
- Never claim an animation was measured when it was only inferred. `evidence` carries that.
- The analysis JSON is capped at 64 KB; animation data is dropped before design data if it is too large.

## Verification

- Run `AnimationReportTests`, `MotionAiTests`, `MotionSpecTests` and `MotionEvidenceReaderTests`.
- Scan a page with known motion (for example one using GSAP) and compare the report with what the site does.
- Not yet checked by eye in a signed-in browser (per `docs/HANDOFF.md`).
