# Handoff note (2026-10-18)

Read this first in a new session. It says where the work stands, not how the app works (see `ARCHITECTURE.md`, `SCANNER-SETTINGS.md`, `backend/README.md`, `CHANGELOG.md`).

## What the app is
Paste a public URL, get Markdown documentation plus a Design tab. Next.js website in `frontend/`, ASP.NET Core API in `backend/`, PostgreSQL (Aiven) for all data, Firebase only for sign-in, Groq (`openai/gpt-oss-20b` for text, `qwen/qwen3.8-27b` for screenshots) for the AI writing. The scanner is Playwright through a locked-down request checker (no private or internal addresses).

## Where things stand
- **Done and committed:** auth, roles in the database, scans, quota, Swagger, API-call capture, design capture, structured Groq output, animation measurement and AI animation notes (the last two came from a separate session the user ran), and the first two features of the current plan: structured design data plus a Design tab with copyable colour swatches and Google Font previews, and component previews (AI-written, sanitised HTML/CSS) in a Components tab.
- **Latest commits:** `f09b001`, `00c6de3`, `6fa2bce` and the notes are all pushed to `origin/main`. The assistant's shell has no GitHub login, so push from GitHub Desktop.
- **Verified:** 992 backend tests pass; website lints and builds; a live scan of tailwindcss.com finds 12 colours. **Not yet checked by eye:** the Design tab on a real scan (click a swatch, check fonts render).

## Next, one feature at a time (commit each, then push)
<<<<<<< HEAD
<<<<<<< HEAD
<<<<<<< HEAD
1. **Component previews: DONE, rebuilt** (`Services/Extraction/Components/` captures element trees with computed styles and `ComponentPreviewBuilder` turns them into HTML/CSS; `Services/Ai/ComponentSanitizer.cs` cleans the result; `components/results/components-panel.tsx` shows them). No AI is involved any more. Not yet compared side by side with a real site in a browser (the browser pane hung). Possible polish: hover/focus capture, icons (SVG), real text widths.
1b. **Design export: DONE** (`frontend/lib/design-export.ts` + `.check.ts`, `components/results/design-export.tsx`): Figma tokens (Tokens Studio JSON), CSS variables and Tailwind v3 config from the Design tab. Not yet imported into Figma by hand; Tokens Studio's import format is from memory of its docs, so confirm with one real import. A direct push into a Figma file would need the Figma connector authorised (claude.ai connector settings).
=======
1. **Component previews: DONE** (`Services/Extraction/Components/`, `Services/Ai/Component*.cs`, `components/results/components-panel.tsx`). Not yet checked by eye in a signed-in browser. Possible polish: hover/focus capture, a kill-switch setting, taller frames for big cards.
>>>>>>> parent of 85b628f (Rebuild component previews from the real page elements so they look like the originals)
=======
1. **Component previews: DONE** (`Services/Extraction/Components/`, `Services/Ai/Component*.cs`, `components/results/components-panel.tsx`). Not yet checked by eye in a signed-in browser. Possible polish: hover/focus capture, a kill-switch setting, taller frames for big cards.
>>>>>>> parent of 85b628f (Rebuild component previews from the real page elements so they look like the originals)
=======
1. **Component previews: DONE** (`Services/Extraction/Components/`, `Services/Ai/Component*.cs`, `components/results/components-panel.tsx`). Not yet checked by eye in a signed-in browser. Possible polish: hover/focus capture, a kill-switch setting, taller frames for big cards.
>>>>>>> parent of 85b628f (Rebuild component previews from the real page elements so they look like the originals)
2. **Animations: DONE** (`Services/Pipeline/AnimationReport.cs`, `AnimationSnippets.cs`, `components/results/animations-panel.tsx`; deterministic estimate and templated GSAP / Framer Motion snippets, no extra AI call). Not yet checked by eye. Old notes follow: capture and AI notes already exist (see commits `f0d3f07`, `ded71e9`, `89ffe56`). Missing: a dedicated tab, and an estimate of whether each effect looks like GSAP or Framer Motion with a copyable snippet. Check what the Results page already shows before building.
Test Groq freely (credits are not real money), but keep calls small.

## How to run and check
- Backend: `dotnet run` in `backend/`. Build and test with `dotnet build ProjectMD.sln` and `dotnet test ProjectMD.sln` (output goes to `backend/artifacts`). If the user's server is running and locks it, add `-p:ArtifactsPath=<scratch folder>\art` to both.
- Real scan without a server: `PROJECTMD_LIVE_SCAN=1` plus `PROJECTMD_LIVE_URL` and `PROJECTMD_LIVE_OUT`, load `Ai__ApiKey`, `Ai__FastModel`, `Scanner__BrowserChannel` from `backend/.env` into the process environment (never print them), then run the test `Live_scan_of_public_site_writes_analysis`.
- Website: `npm run dev` in `frontend/`. `NEXT_PUBLIC_SCAN_PROVIDER=api` switches it to the real server.
- Swagger: http://localhost:5080/swagger (Development only); the account menu has a dev-only "Copy API token" button.

## Rules and gotchas
- **Secrets** live in `backend/.env` and `frontend/.env.local` (both ignored by git). Never print or commit them. The Aiven password was pasted in an earlier chat; the user chose to leave it for now. The user's own password and email were also shared once; never use them to sign in.
- **Do not start the server from the assistant's shell.** Stopping it leaves unkillable "ghost" processes that lock build files. Use tests and the live test instead.
- **Commit style:** one commit per feature, short plain-language message (what and why, little jargon), a matching entry in `CHANGELOG.md`, and **no `Co-Authored-By` line**. Stage specific paths only (never `.claude/`, `node_modules`, `.env*`).
- **The ECC plugin's gate hook** blocks the first attempt of an edit or git command until the assistant states the affected files, a rollback and the user's instruction, then allows the identical retry.
- **Windows PowerShell traps:** `Rd` is an alias for delete (it once emptied files); XML comments cannot contain `--`; use absolute paths with .NET file calls; git is at `%LOCALAPPDATA%\GitHubDesktop\app-*\resources\app\git\cmd\git.exe`.
- **Database changes:** the assistant applies migrations to Aiven after review (`dotnet ef database update`), tells the user, and never touches data. Admin email list is in `backend/.env`.
- Agent briefs for past work are in `agent-skills/` (00 to 26).
