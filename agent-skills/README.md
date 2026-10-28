# Project MD: agent skills

Skill files for building the Project MD frontend with several agents in parallel. Each file is one agent's brief: what it owns, what it must read, the rules, and when it is done.

Project MD turns a public URL into structured Markdown documentation. The frontend is Next.js 16 (App Router), React 19 and Tailwind 4, in `frontend/`.

## Source of truth

1. **Wireframes v2** (`Project MD Web Application*.png`, 8 screens): icon rail, hero, processing card, two-pane Results with Design/Components/Animations tabs, History. They win over everything else. The older design-system artifact (https://claude.ai/artifact/P5okfTXdafYjV4u2jySAPw, deep-violet v1) is **stale**; use it only for component behaviour, never for colours or layout. Tokens live in `00-foundation-tokens.md`.
2. **`frontend/AGENTS.md`**: this is not the Next.js you know. Read the matching guide in `frontend/node_modules/next/dist/docs/` before writing routing, fonts, metadata or data code.

## The skills

| # | File | Owns | Depends on |
| --- | --- | --- | --- |
| 00 | [00-foundation-tokens.md](00-foundation-tokens.md) | `app/globals.css`, fonts in `app/layout.tsx` | none |
| 01 | [01-app-shell.md](01-app-shell.md) | `components/shell/*`, `app/layout.tsx` body | 00 |
| 02 | [02-ui-components.md](02-ui-components.md) | `components/ui/*` | 00 |
| 03 | [03-data-and-state.md](03-data-and-state.md) | `lib/*`, `components/scan-provider.tsx` | none |
| 04 | [04-screen-generate.md](04-screen-generate.md) | `app/(workspace)/page.tsx`, hero backdrop | 01, 02, 03 |
| 05 | [05-screen-processing.md](05-screen-processing.md) | `app/(workspace)/processing/*` | 01, 02, 03 |
| 06 | [06-screen-results.md](06-screen-results.md) | `app/(workspace)/results/*`, `components/results/*` | 01, 02, 03 |
| 07 | [07-screen-history.md](07-screen-history.md) | `app/(workspace)/history/*`, `lib/download.ts` | 01, 02, 03 |
| 08 | [08-qa-review.md](08-qa-review.md) | read-only, reports defects | all |
| 09 | [09-auth-firebase.md](09-auth-firebase.md) | `app/functions/{firebase,auth}`, `app/layout.tsx`, route groups, `.env.example` | none |
| 10 | [10-rbac.md](10-rbac.md) | `app/functions/rbac/*` | contract with 09 |
| 11 | [11-data-provider.md](11-data-provider.md) | `app/functions/data/*`, `lib/scan.ts`, `lib/history-store.ts`, `components/scan-provider.tsx` | contract with 09, 10 |
| 12 | [12-screen-auth.md](12-screen-auth.md) | `app/(auth)/*`, `components/auth/*` | 09, 10 |
| 13 | [13-workspace-auth-integration.md](13-workspace-auth-integration.md) | shell user menu, banner, permission gating on screens | 09, 10, 11, 12 |
| 14 | [14-backend-foundation.md](14-backend-foundation.md) | `backend/` scaffold, models, DbContext, DTOs, migration | none |
| 15 | [15-backend-auth.md](15-backend-auth.md) | `backend/.../Auth/*`, `MeController` | 14 |
| 16 | [16-backend-scans.md](16-backend-scans.md) | `ScansController`, `Services/*`, backend tests | 14, 15 |
| 17 | [17-frontend-api-connect.md](17-frontend-api-connect.md) | frontend api client, api provider, remote history, usage | contract from 14-16 |
| 18 | [18-backend-extraction.md](18-backend-extraction.md) | `Services/Extraction/*` (Playwright fetch, HTML reduction, tech fingerprint) | 14, 16 |
| 19 | [19-backend-ai-budget.md](19-backend-ai-budget.md) | `Services/Ai/*`, `Services/Pipeline/*`, cache and usage tables | 18 contract |
| 20 | [20-backend-roles.md](20-backend-roles.md) | roles stored in the database, admin-email bootstrap, admin endpoints | after 18, 19 |
| 21 | [21-frontend-roles-admin.md](21-frontend-roles-admin.md) | role from the server, admin Users page | contract from 20 |
| 23 | [23-backend-api-capture.md](23-backend-api-capture.md) | record the page's API calls (no secrets or values) | 18 |
| 24 | [24-backend-design-capture.md](24-backend-design-capture.md) | extract colours, fonts, spacing, CSS variables | 18 |
| 25 | [25-backend-structured-output.md](25-backend-structured-output.md) | Groq returns structured JSON, server renders the Markdown | 19, 22, contract from 23-24 |

**Design v2 note:** the hero photo is a placeholder; put the real image at `frontend/public/hero.jpg`.

Extraction waves: 18 and 19 in parallel (19 codes to 18's contract).

Backend waves: **A**: 14. **B** (parallel): 15, 16, 17. Then migrate, smoke-test and 08.

Auth waves: **A** (parallel): 09, 10, 11. **B** (parallel): 12, 13. Then 08 again. 09 takes over the `<body>` of `app/layout.tsx` and moves the screens into `app/(workspace)/` (this supersedes the layout ownership in 01).

## Running them in parallel

- **Wave 1** (parallel): 00, 02, 03. They share no files.
- **Wave 2** (parallel): 01, 04, 05, 06, 07, once wave 1 has merged.
- **Wave 3**: 08.

## Rules for every agent

- Edit only the files your skill owns. Need something from another area? Say so in your report; do not edit it.
- Use token classes (`bg-bg-surface`, `text-ink-muted`, `ts-label`), never raw hex or arbitrary values for colour, radius or type.
- No new dependencies without a stated reason. Prefer the platform and the standard library.
- Run `npm run lint` and `npm run build` in `frontend/` before you report done.
- Report what you built, what you assumed, and what is left. Do not claim a check you did not run.
