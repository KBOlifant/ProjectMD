# Scanner settings: screenshots, images and animation detection

All of these live in one file: **`backend/ProjectMD.Api/appsettings.json`**, in the `"Motion"` section.
Change a number, save, and restart the backend (`dotnet run` in the `backend` folder). Nothing else
needs to change.

You can also override any of them without editing the file, as an environment variable or a line in
`backend/.env`, by writing the section and name with two underscores, for example
`Motion__ScrollFrames=8`.

If a value is out of range, the scanner refuses to start a scan and the log says
"Motion settings are out of range".

## What happens during a scan

1. The page is opened in a browser window of `ViewportWidth` x `ViewportHeight`.
2. Images and fonts are loaded (if `LoadImagesAndFonts` is on) so the page looks real.
3. The scanner reads which animation libraries are on the page and the animations the browser is
   running (with their real durations and easing curves).
4. It takes `ScrollFrames` screenshots, scrolling `ScrollStepPixels` down and waiting
   `FrameDelayMs` between each.
5. It reads the animations again (scroll animations only exist once you reach them).
6. The screenshots are laid out in grids ("contact sheets") of `SheetColumns` x `SheetRows`.
7. In `ai` mode, the first screenshot and the contact sheets go to the vision model
   (`VisionModel`), which lists the animations it can see.

The screenshots are kept in memory for that one scan only. They are never saved in the database.

## The settings

### Switches

| Setting | Default | What it does |
| --- | --- | --- |
| `Enabled` | `true` | Master switch. `false` = no screenshots, no images, no animation facts (scans work exactly as before). |
| `AiEnabled` | `true` | Send the screenshots to the vision model. `false` keeps the measured facts in the document but makes no extra AI call. |
| `LoadImagesAndFonts` | `true` | Load images and fonts so screenshots look like the real page. They still go through the same safety checks as everything else. |

### Browser window

| Setting | Default | What it does |
| --- | --- | --- |
| `ViewportWidth` | `1440` | Window width in pixels (320 to 3840). Award-style sites often turn effects off on small screens, so keep it desktop-sized. |
| `ViewportHeight` | `900` | Window height in pixels (320 to 2400). |

### Screenshots

| Setting | Default | What it does |
| --- | --- | --- |
| `ScrollFrames` | `6` | How many screenshots, the first screen included (1 to 24). More frames see more of a long page but take longer. |
| `ScrollStepPixels` | `760` | How far to scroll between screenshots (50 to 5000). A bit less than `ViewportHeight` keeps a small overlap between frames. |
| `FrameDelayMs` | `700` | How long to wait after each scroll before the screenshot (0 to 10000). Raise it if scroll animations are caught half-way. |
| `ScreenshotQuality` | `60` | JPEG quality (1 to 100). Lower = smaller images. |
| `SheetColumns` | `3` | Frames per row on a contact sheet (1 to 6). |
| `SheetRows` | `2` | Rows per contact sheet (1 to 6). Frames per sheet = columns x rows. |
| `CaptureTimeoutSeconds` | `20` | Time limit for all screenshots of one page (2 to 120). When it runs out, the frames taken so far are kept. |

### Limits for images and fonts

| Setting | Default | What it does |
| --- | --- | --- |
| `MaxImageAndFontBytes` | `15728640` (15 MB) | Most image and font data one page may load. Counted separately from scripts, so images can never block them. |
| `MaxImageAndFontRequests` | `150` | Most image and font requests one page may make. |

### The vision model

| Setting | Default | What it does |
| --- | --- | --- |
| `VisionModel` | `qwen/qwen3.8-27b` | The Groq model that reads the screenshots. It must accept images. The text notes still use `Ai:FastModel`. |
| `MaxImagesPerRequest` | `3` | Most images per request. Groq's `qwen/qwen3.8-27b` accepts 3. Image 1 is the first screen; the rest are contact sheets. |
| `TokensPerImage` | `2048` | What Groq charges per image, in input tokens. Only used to check the daily budget before the call. |
| `MaxOutputTokens` | `900` | Longest answer the model may give. |
| `ReasoningEffort` | `none` | How much the model thinks first: `none` (fastest, cheapest), `low`, `medium`, `high` or `default`. |

## Other settings changed for this

| Setting | Was | Now | Why |
| --- | --- | --- | --- |
| `Scanner:OverallTimeoutSeconds` | `45` | `75` | Screenshots add up to 20 seconds to a scan. |
| `Ai:DailyTokenCapPerUser` | `20000` | `60000` | A scan in `ai` mode now makes two AI calls; the vision one counts about 5,000 to 7,000 tokens (each image counts 2,048). At the old limit a person could only do about two scans a day. |

## Cost

In `ai` mode a scan now makes up to two Groq calls: the text notes (as before) and the vision call
(about 2 to 3 images plus a short list of facts). Both are cached for `Ai:CacheDays` days, so
scanning the same page again costs nothing, and both are counted against the daily limits before
they are made.

## Sites that block scanners

Some sites (often behind Cloudflare) answer with a "checking your browser" page instead of their
content. The scan now stops with the message "This site blocks automated scanners..." instead of
documenting the block page. Project MD does not try to get around this.
