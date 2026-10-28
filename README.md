# Project MD

**Have you ever seen a design system on a website, or an element, styled so well that you would like to reuse it but didn't know where to start?**

Project MD is for full-stack developers who look at a website and wonder how it was made. It opens the page in a temporary browser, works out the tech stack, API calls, design system, components and animations, and uses Groq AI to explain what it found. You get it all back as a single Markdown (`.md`) document.

<p align="left">
  <img src="https://skillicons.dev/icons?i=nextjs,react,ts,tailwind,dotnet,cs,postgres,firebase" alt="Tech stack icons" />
</p>

---

## Table of contents

- [Project overview](#project-overview)
- [Getting started](#getting-started)
  - [Clone the repository](#clone-the-repository)
  - [Backend setup](#backend-setup)
  - [Database setup (hosted PostgreSQL)](#database-setup-hosted-postgresql)
  - [Testing the endpoints in Swagger](#testing-the-endpoints-in-swagger)
  - [Running the backend tests](#running-the-backend-tests)
  - [Frontend setup: development](#frontend-setup-development)
  - [Frontend setup: production](#frontend-setup-production)
- [Features and usage](#features-and-usage)
- [API endpoints](#api-endpoints)
- [Mockups](#mockups)
- [Technologies used](#technologies-used)
- [Project structure](#project-structure)
- [Acknowledgements](#acknowledgements)
- [License](#license)

---

## Project overview

### Purpose

Every website is a finished product, and the way it was built is hidden behind it. Project MD is for developers who want to know what is behind that: which framework it uses, which APIs it calls, which colours and fonts make up its design, and how its animations move. Paste in an address and Project MD scans the page and writes it up as a readable, well-organised `.md` file. Groq AI explains each finding in plain language, so you learn why the site is built that way as well as what is there.

### How a scan works

1. **Fetch.** Playwright renders the page in headless Chromium. Every request the page makes goes through a guard that blocks private networks, which protects the server against SSRF.
2. **Extract.** Plain code (no AI) reduces the page and fingerprints the technologies. It also records the API calls, samples the design system and reads the components and animations.
3. **Explain.** Groq writes short explanations as strict, schema-checked JSON. A vision model reads screenshots to describe the animations.
4. **Build.** The server puts everything together into a fixed Markdown layout and saves the scan.

The backend's `Scanner:Mode` setting controls how much of this runs:

| Mode | What happens |
|---|---|
| `static` | Mock scans only. No network, no AI. |
| `extract` | Real fetch and extraction, no AI. |
| `ai` | Everything in `extract`, plus the Groq explanations. |

---

## Getting started

### Clone the repository

```bash
git clone https://github.com/KBOlifant/ProjectMD.git
cd ProjectMD
```

### Backend setup

The backend is an ASP.NET Core Web API in [`backend/`](backend/).

**1. Create your private settings file.** Copy the example file. The copy, `backend/.env`, is ignored by git.

```bash
cd backend
cp .env.example .env        # PowerShell: Copy-Item .env.example .env
```

Fill it in (placeholders shown):

```ini
# PostgreSQL (see "Database setup" below)
ConnectionStrings__Default=Host=YOUR_HOST;Port=5432;Database=YOUR_DB;Username=YOUR_USER;Password=YOUR_PASSWORD;SSL Mode=Require

# Your email becomes admin when you sign in with it verified
Auth__AdminEmails__0=you@example.com

# static | extract | ai
Scanner__Mode=ai
# Optional: use an installed browser instead of Playwright's Chromium (chrome | msedge)
Scanner__BrowserChannel=chrome

# Groq (server-side only, never commit it)
Ai__ApiKey=YOUR_GROQ_API_KEY
Ai__FastModel=openai/gpt-oss-20b

# Optional: Cloudinary for scan screenshots
# Cloudinary__Url=cloudinary://<api_key>:<api_secret>@<cloud_name>
```

Real environment variables override `.env`. With no Groq key, `ai` mode still works: it behaves like `extract`, and each document ends with a "Basic scan" note.

**2. Restore tools and build.**

```bash
dotnet tool restore            # installs the pinned dotnet-ef
dotnet build ProjectMD.sln
```

**3. Install the page renderer (once per machine).** Skip this if you set `Scanner__BrowserChannel` to `chrome` or `msedge`.

```bash
pwsh artifacts/bin/ProjectMD.Api/debug/playwright.ps1 install chromium
```

On Linux, add `--with-deps`. If you don't have `pwsh`, use the Playwright CLI instead: `dotnet tool install --global Microsoft.Playwright.CLI`, then `playwright install chromium`.

**4. Run it.**

```bash
dotnet run                     # from backend/; starts on http://localhost:5080
```

**5. Check it is up.**

```bash
curl http://localhost:5080/api/health
# { "status": "ok" }
```

> If a build fails with "file is locked by ProjectMD.Api.exe", an older copy of the server is still running. Stop it with Ctrl+C in its terminal (or `taskkill /F /IM ProjectMD.Api.exe`) and build again.

For every setting, see [backend/README.md](backend/README.md).

### Database setup (hosted PostgreSQL)

The API uses Entity Framework Core with PostgreSQL (Npgsql).

**1. Create a database** with your hosting provider (Neon, Supabase, Render, Azure and so on) and copy its host, port, database name, user and password.

**2. Add the connection string** to `backend/.env`. Hosted databases need SSL:

```ini
ConnectionStrings__Default=Host=<host>;Port=5432;Database=<database>;Username=<user>;Password=<password>;SSL Mode=Require
```

**3. Apply the migrations.** The API never changes the database by itself on startup, so you do this step yourself. Run from `backend/`:

```bash
dotnet ef migrations script --project ProjectMD.Api      # optional: review the SQL first
dotnet ef database update --project ProjectMD.Api
```

This creates the following tables:

| Table | Holds |
|---|---|
| `AppUsers` | Firebase uid, email, role (`viewer`, `member` or `admin`), sign-up and last-seen times |
| `Scans` | URL, host, tech, the Markdown document, the design, API, component and animation analysis, screenshot URL, and a soft-delete time |
| `ExtractionCache` | Cached AI answers for identical page content (no user data) |
| `LlmUsage` | Token counts per AI call, used for the daily caps |

**4. Check the connection.**

```bash
curl "http://localhost:5080/api/health?db=true"
# 200 when the database is reachable, 503 when it is not
```

**Changing the models later:** run `dotnet ef migrations add <Name> --project ProjectMD.Api --output-dir Data/Migrations`, review the result, then run `database update` again.

Tokens last about an hour. If you start getting `401`, copy a fresh one.

### Running the backend tests

```bash
cd backend
dotnet test ProjectMD.sln
```

The tests use fixture HTML, a local test web server and SQLite, so they need no network, browser or real database. Some opt-in tests are switched on with environment variables:

| Variable | Runs |
|---|---|
| `PROJECTMD_BROWSER_TESTS=1` | Tests that drive a real browser |
| `PROJECTMD_LIVE_SCAN=1` | A full scan of a real public site (`PROJECTMD_LIVE_URL`, default `https://tailwindcss.com`) |
| `PROJECTMD_LIVE_GROQ=1` | One tiny real Groq call (needs `Ai__ApiKey`) |

### Frontend setup: development

The frontend is a Next.js app in [`frontend/`](frontend/).

**1. Install dependencies.**

```bash
cd frontend
npm install
```

**2. Create `.env.local`** from the example file:

```bash
cp .env.example .env.local     # PowerShell: Copy-Item .env.example .env.local
```

Fill in your Firebase web config, which you can find in Firebase console > Project settings > Your apps:

```ini
NEXT_PUBLIC_FIREBASE_API_KEY=
NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN=
NEXT_PUBLIC_FIREBASE_PROJECT_ID=
NEXT_PUBLIC_FIREBASE_STORAGE_BUCKET=
NEXT_PUBLIC_FIREBASE_MESSAGING_SENDER_ID=
NEXT_PUBLIC_FIREBASE_APP_ID=
```

**3. Choose where scans come from:**

| `NEXT_PUBLIC_SCAN_PROVIDER` | Behaviour |
|---|---|
| `static` *(default)* | Makes mock scans in the browser and keeps history in localStorage. No backend needed, which is handy for UI work. |
| `api` | Uses the ASP.NET Core backend for real scans. Also set `NEXT_PUBLIC_API_URL` (default `http://localhost:5080`). |

```ini
NEXT_PUBLIC_SCAN_PROVIDER=api
NEXT_PUBLIC_API_URL=http://localhost:5080
```

Other development-only options:
- `NEXT_PUBLIC_USE_AUTH_EMULATOR=true` uses the local Firebase Auth emulator.
- `NEXT_PUBLIC_DEV_ROLE=viewer|member|admin` lets you try the UI as a different role.

**4. Start the dev server.**

```bash
npm run dev
```

Open **http://localhost:3000**. Restart `npm run dev` whenever you change `.env.local`.

### Frontend setup: production

**1. Set production values.** `NEXT_PUBLIC_*` values are baked into the bundle at build time, so set them before you build. Point them at the deployed backend:

```ini
NEXT_PUBLIC_SCAN_PROVIDER=api
NEXT_PUBLIC_API_URL=https://your-backend.example.com
```

`NEXT_PUBLIC_DEV_ROLE` is ignored in production builds.

**2. Build and start.**

```bash
npm run build
npm start                      # serves on http://localhost:3000 by default
```

**3. Allow the frontend in the backend's CORS list**, and run the backend as Production:

```ini
Cors__AllowedOrigins__0=https://your-frontend.example.com
ASPNETCORE_ENVIRONMENT=Production
```

In Production, Swagger is switched off.

**4. Add your domain to Firebase.** In Firebase console > Authentication > Settings > Authorized domains, add the frontend's domain.

---

## Features and usage

Use the sidebar to move between **Generate**, **Processing**, **Results**, **History** and **Admin** (admins only).

- **Sign up / log in:** create an account with email and password, then verify your email. You can't run scans until your email is verified.
- **Generate:** paste a URL and click **Analyze website**.
- **Processing:** watch the scan's progress, then open the results. Click **Cancel** to stop.
- **Website breakdown (Results):** the sidebar shows the tech fingerprint, scan time and confidence. The tabs are:
  - **Preview / Raw:** the Markdown breakdown, rendered or as source.
  - **Design:** colours, fonts, spacing and CSS variables. Click a swatch to copy it.
  - **Components:** HTML and CSS previews of the page's elements, ready to copy.
  - **Animations:** what each animation does and what triggers it, with GSAP and Framer Motion snippets.
- **File generation:** **Copy** or **Download** the breakdown as `<host>.md`. In the Design tab, **Export** the design system as Figma tokens, CSS variables or a Tailwind config.
- **History:** reopen past scans, with a screenshot preview when you hover over one. Clearing your history does not reset your usage.
- **Roles:** a *viewer* can read and export scans. A *member* (the default) can also run scans. An *admin* can also change users' roles under **Admin > Users**.

---

## API endpoints

Base URL in development: `http://localhost:5080`. Every endpoint except `/api/health` needs an `Authorization: Bearer <Firebase ID token>` header. Responses are camelCase JSON, and errors are [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457).

| Method | Endpoint | Permission | Description |
|---|---|---|---|
| `GET` | `/api/health` | Public | Returns `{ "status": "ok" }`. Add `?db=true` to also check the database (`503` if unreachable) |
| `GET` | `/api/me` | Signed in | Your `uid`, `email`, `emailVerified`, `role` and `usage` |
| `GET` | `/api/scans` | `scan:read` | List your scans, newest first |
| `GET` | `/api/scans/{id}` | `scan:read` | Get one scan, with its Markdown and analysis |
| `POST` | `/api/scans` | `scan:create` + verified email | Run a scan. Body: `{ "url": "https://example.com" }` |
| `DELETE` | `/api/scans` | `history:clear` | Clear your history (soft delete) |
| `GET` | `/api/admin/users` | `admin:access` | List up to 200 users, newest first |
| `PUT` | `/api/admin/users/{id}/role` | `admin:access` | Change a user's role. Body: `{ "role": "viewer" \| "member" \| "admin" }` |

Error responses include a `reason` field that says what went wrong:

| `reason` | Status | Meaning |
|---|---|---|
| `url` | 400 | The address is not a valid public URL |
| `extract` | 400 / 502 | The address is not a public HTML page, or the site or renderer failed |
| `rate` | 429 | Too many scans in a short time |
| `quota` | 429 | The usage allowance is used up |
| `budget` | 429 | The daily AI budget is used up (only when `Ai:FallbackOnBudget` is `false`) |
| `unverified` | 403 | You need to verify your email first |
| `role` | 403 | Your role does not allow this |
| `self` / `last-admin` | 400 / 409 | Admin role-change guards |

---

## Mockups

> _Screenshots and mockups go here._

<!--
Put images in docs/mockups/ and uncomment the lines below.

### Generate
![Generate page](docs/mockups/generate.png)

### Processing
![Processing page](docs/mockups/processing.png)

### Results: Preview
![Results preview tab](docs/mockups/results-preview.png)

### Results: Design
![Results design tab](docs/mockups/results-design.png)

### Results: Components
![Results components tab](docs/mockups/results-components.png)

### Results: Animations
![Results animations tab](docs/mockups/results-animations.png)

### History
![History page](docs/mockups/history.png)

### Admin
![Admin users page](docs/mockups/admin.png)

### Sign in / Sign up
![Auth pages](docs/mockups/auth.png)
-->

---

## Technologies used

### Frontend

![Next.js](https://img.shields.io/badge/Next.js_16-000000?style=for-the-badge&logo=nextdotjs&logoColor=white)
![React](https://img.shields.io/badge/React_19-20232A?style=for-the-badge&logo=react&logoColor=61DAFB)
![TypeScript](https://img.shields.io/badge/TypeScript-3178C6?style=for-the-badge&logo=typescript&logoColor=white)
![Tailwind CSS](https://img.shields.io/badge/Tailwind_CSS_4-06B6D4?style=for-the-badge&logo=tailwindcss&logoColor=white)
![Framer Motion](https://img.shields.io/badge/Framer_Motion-0055FF?style=for-the-badge&logo=framer&logoColor=white)
![Locomotive Scroll](https://img.shields.io/badge/Locomotive_Scroll-111111?style=for-the-badge)

- **Next.js / React:** the app, its routing and pages
- **TypeScript:** type-safe frontend code
- **Tailwind CSS:** styling and design tokens
- **Framer Motion / Locomotive Scroll:** UI animation and smooth scrolling

### Backend

![.NET](https://img.shields.io/badge/.NET_10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge)
![EF Core](https://img.shields.io/badge/EF_Core_10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![Swagger](https://img.shields.io/badge/Swagger-85EA2D?style=for-the-badge&logo=swagger&logoColor=black)
![Playwright](https://img.shields.io/badge/Playwright-2EAD33?style=for-the-badge)
![AngleSharp](https://img.shields.io/badge/AngleSharp-444444?style=for-the-badge)

- **ASP.NET Core Web API (.NET 10, C#):** the REST API, auth policies and rate limiting
- **Entity Framework Core:** data access and migrations
- **Swashbuckle (Swagger):** interactive API docs in development
- **Playwright:** renders the scanned page in a guarded headless browser
- **AngleSharp:** HTML and CSS parsing

### Database and storage

![PostgreSQL](https://img.shields.io/badge/PostgreSQL-4169E1?style=for-the-badge&logo=postgresql&logoColor=white)
![Cloudinary](https://img.shields.io/badge/Cloudinary-3448C5?style=for-the-badge&logo=cloudinary&logoColor=white)

- **PostgreSQL (Npgsql):** users, scans, AI cache and usage
- **Cloudinary:** one screenshot per scan (optional)

### AI

![Groq](https://img.shields.io/badge/Groq-F55036?style=for-the-badge)

- **Groq:** `openai/gpt-oss-20b` writes the explanations through strict structured output, and a vision model reads screenshots for the animation notes

### Authentication

![Firebase](https://img.shields.io/badge/Firebase_Auth-FFCA28?style=for-the-badge&logo=firebase&logoColor=black)

- **Firebase Authentication:** sign-up, sign-in, email verification and ID tokens. The backend checks the tokens without a service account.

### Testing and tooling

![xUnit](https://img.shields.io/badge/xUnit-512BD4?style=for-the-badge)
![SQLite](https://img.shields.io/badge/SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white)
![ESLint](https://img.shields.io/badge/ESLint-4B32C3?style=for-the-badge&logo=eslint&logoColor=white)

- **xUnit + ASP.NET Core test host + SQLite:** backend unit and integration tests
- **ESLint:** frontend linting (`npm run lint`)

---

## Project structure

```
ProjectMD/
├── backend/
│   ├── ProjectMD.sln
│   ├── Run.csproj                 # lets you type `dotnet run` in backend/
│   ├── .env.example               # copy to .env
│   ├── ProjectMD.Api/
│   │   ├── Auth/                  # Firebase JWT, roles, permissions
│   │   ├── Controllers/           # Health, Me, Scans, Admin
│   │   ├── Data/                  # AppDbContext + Migrations
│   │   ├── Dtos/  Models/
│   │   └── Services/
│   │       ├── Ai/                # Groq client, prompts, budget guard, cache
│   │       ├── Extraction/        # Playwright fetch, SSRF guard, tech, design, API, motion capture
│   │       └── Pipeline/          # scan pipeline + Markdown document builder
│   └── ProjectMD.Api.Tests/       # xUnit tests
├── frontend/
│   ├── app/                       # Next.js routes: (auth) and (workspace) pages
│   ├── components/                # results tabs, shell, auth, admin, ui
│   └── lib/                       # scan types, design export, helpers
├── docs/                          # architecture and how-it-works guides
└── CHANGELOG.md
```

Further reading (What Claude Opus 5.5 wrote):
- [backend/README.md](backend/README.md): every setting, the data model, the AI credit controls and the security notes
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): diagrams of the flow
- [docs/how-it-works/](docs/how-it-works/): step-by-step and visual guides

---

## Acknowledgements

<!-- Add, remove or reword entries as needed. -->

- [Groq](https://groq.com/) for fast AI inference
- [Firebase](https://firebase.google.com/) for authentication
- [Playwright](https://playwright.dev/) for headless browser rendering
- [Neu Prompter](https://www.1001fonts.com/) font, used under its EULA (see `frontend/app/assets/neu-prompter/`)
- [shields.io](https://shields.io/) and [skillicons.dev](https://skillicons.dev/) for the badges and icons
- _Lecturers / mentors: add names here_
- _Tutorials, articles or resources that helped: add links here_
- _Anyone else you would like to thank_

---

## License

This project is licensed under the MIT License.
Copyright (c) 2026 KBOlifant.
You are free to use, copy, modify and distribute it, provided this notice is kept.
