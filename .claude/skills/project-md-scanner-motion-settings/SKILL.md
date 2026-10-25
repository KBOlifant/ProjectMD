---
name: project-md-scanner-motion-settings
description: Use when changing the screenshot and animation-detection settings (the Motion section), their limits, or the scanner's timeouts.
---

# Scanner Motion Settings

Settings class: `Services/Extraction/Visual/MotionOptions.cs`. Values: the `Motion` section of `backend/ProjectMD.Api/appsettings.json`. The full table of every setting, its range and its effect is in `docs/SCANNER-SETTINGS.md`; read that before changing a number.

## How to change a value

Edit `appsettings.json` and restart the backend, or override with an environment variable or a line in `backend/.env` using two underscores: `Motion__ScrollFrames=8`. Out-of-range values stop scans with "Motion settings are out of range". The log says which.

## What a scan does with them

1. Open the page at `ViewportWidth` x `ViewportHeight` (default 1440 x 900, desktop on purpose, because effect-heavy sites turn effects off on small screens).
2. Load images and fonts if `LoadImagesAndFonts`, under the same safety checks as every request and the byte and request caps.
3. Read the animation libraries and the animations the browser is running.
4. Take `ScrollFrames` screenshots, scrolling `ScrollStepPixels` and waiting `FrameDelayMs` each time, within `CaptureTimeoutSeconds`.
5. Read the animations again (scroll animations only exist once reached).
6. Lay the frames out in contact sheets (`SheetColumns` x `SheetRows`).
7. In `ai` mode, send the first screenshot and the sheets to the vision model with the measured facts.

Screenshots stay in memory for that scan and are never saved.

## Related settings

- `Scanner:OverallTimeoutSeconds` is 75 so screenshots (up to about 20s) fit.
- `Ai:DailyTokenCapPerUser` is 60000 because the vision call is about 5000 to 7000 tokens (each image counts `TokensPerImage`, 2048).
- `MaxImagesPerRequest` is 3, the most the configured vision model accepts.

## Rules

- Keep numbers in configuration. Do not hard-code a limit in code.
- When you change a default, update `appsettings.json`, `MotionOptions` validation, and `docs/SCANNER-SETTINGS.md` together.
- Missing animation text is explained in the backend log ("Visual capture for ...", "Vision AI skipped: ...", provider refusal details). Check the log before changing code.
- Do not try to defeat bot protection to get more screenshots.

## Verification

- Tests: `MotionEvidenceReaderTests`, `VisualCaptureTests`, `MotionAiTests`.
- A live scan of a known animated site shows the "Visual capture" log line and an Animations tab with items.
