# Project MD

**Paste a website's address and find out how it was built.**

Project MD is for full-stack developers who look at a website and wonder how it was made. It opens the page in a real browser, works out the tech stack, API calls, design system, components and animations, and uses Groq AI to explain what it found. You get it all back as a single Markdown (`.md`) document.

<p align="left">
  <img src="https://skillicons.dev/icons?i=nextjs,react,ts,tailwind,dotnet,cs,postgres,firebase" alt="Tech stack icons" />
</p>

---

## Table of contents

- [Project overview](#project-overview)
- [Getting started](#getting-started)
  - [Prerequisites](#prerequisites)
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

### Key features

- **Website breakdown.** The tech stack (about 45 detectable technologies), architecture notes, first-party API endpoints with their response shapes, third-party services, page structure and developer tips.
- **Design system capture.** The site's colours (with guessed roles), fonts, corner radii, shadows, spacing, CSS variables and breakpoints, shown as swatches and live font previews.
- **Component previews.** Small HTML and CSS rebuilds of the page's buttons, inputs, cards, navigation bars, headings and badges, ready to copy.
- **Animation analysis.** Each animation's trigger, a guess at the library behind it, and starter snippets for GSAP and Framer Motion.
- **File generation.** Copy or download the whole breakdown as a `.md` file, and export the design system as Figma tokens, CSS variables or a Tailwind config.
- **Scan history.** Every scan is saved to your account, with a screenshot preview of each one.
- **Accounts and roles.** Firebase sign-in, email verification, and three roles (viewer, member and admin) with an admin screen for managing users.
- **Cost controls.** AI answers are cached, every user has a daily token cap and there is a global one too, and if the AI is unavailable the scan still finishes with a basic document instead of failing.

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

### Prerequisites

| Tool | Notes |
|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Backend |
| [Node.js 20.9+](https://nodejs.org/) and npm | Frontend |
| A hosted PostgreSQL database | For example [Neon](https://neon.tech) or [Supabase](https://supabase.com) |
| A [Firebase](https://console.firebase.google.com/) project | Email/password sign-in |
| A [Groq API key](https://console.groq.com/keys) | Needed for `ai` mode |
| [Cloudinary](https://cloudinary.com/) account *(optional)* | Stores one screenshot per scan |
| Google Chrome or Microsoft Edge *(optional)* | Can replace Playwright's own Chromium |

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

### Testing the endpoints in Swagger

Swagger only runs in Development, which is what `dotnet run` uses.

1. Start the backend and open **http://localhost:5080/swagger**.
2. `GET /api/health` needs no sign-in, so you can try it straight away.
3. Every other endpoint needs a Firebase ID token. To get one:
   1. Start the frontend with `npm run dev` (see below) and sign in.
   2. Open the account menu (top right) and click **Copy API token**.
   3. Back in Swagger, click **Authorize**, paste the token and confirm.
4. Expand an endpoint, click **Try it out**, then **Execute**.

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

**Deploying to Vercel:** import the repository, set the root directory to `frontend`, add the `NEXT_PUBLIC_*` variables in the project settings, and deploy. Host the backend anywhere that runs .NET 10 and can install a browser for Playwright, and keep its secrets in that host's environment variables.

---

## Features and usage

The app has a sidebar for navigation: **Generate**, **Processing**, **Results**, **History**, and **Admin** (admins only).

### Sign up and sign in

1. Go to **Sign up** and create an account with your email and password.
2. Check your inbox and click the verification link. **You need a verified email to run scans**, and until you verify, a banner reminds you.
3. You can use **Log in** and **Reset password** at any time. The account menu in the top right shows your role, and has the **Copy API token** button (dev only) and sign-out.

### Generate a scan

1. On the **Generate** page, paste a website address (for example `https://tailwindcss.com`).
2. Click **Analyze website**. If the address is invalid, you get a message right away.
3. The usage meter shows how many scans you have made and how much AI budget you have used.

### Processing

You are taken to the **Processing** page, where a progress card steps through the scan stages. Click **Cancel** at any time to go back. When the scan is ready, open the results.

### Website breakdown (Results)

The Results page has two panes:

- **Sidebar:** a screenshot thumbnail, a **tech fingerprint** (detected technologies, with the important ones highlighted) and scan details: pages crawled, scan time, technology count and a **confidence** score. Confidence estimates how much usable information the page gave; it is not a measured accuracy.
- **Tabs:**

| Tab | What it shows | How to use it |
|---|---|---|
| **Preview** | The rendered Markdown breakdown | Read the overview, stack, architecture, API endpoints, page structure and developer tips |
| **Raw** | The Markdown source | Read or copy the exact file contents |
| **Design** | Colour swatches, live font previews, radii, shadows, spacing and CSS variables | Click a swatch to copy its hex code. Copy font CSS or Google Fonts links. Use **Export** for design tokens |
| **Components** | Sandboxed HTML/CSS previews of buttons, inputs, cards, nav bars, headings and badges | Switch between page, light and dark backgrounds. Copy the HTML, the CSS, or both |
| **Animations** | Each animation's name, location, trigger (load, scroll, hover…) and AI description, plus the libraries found on the page | Switch between GSAP and Framer Motion and copy the starter snippet |

Use the left and right arrow keys to move between tabs.

### File generation

- **Copy** puts the whole Markdown document on your clipboard.
- **Download** saves it as `<host>.md`.
- The document always follows the same layout:
  - YAML front matter
  - Overview
  - Tech stack
  - Architecture
  - API endpoints (with response shapes and third-party services)
  - Design system
  - Page structure
  - Developer tips
  - Footer

  Sections with no data are left out.
- **Design export** (Design tab) offers three formats, each of which you can copy or download:
  - **Figma tokens**: JSON in the Tokens Studio format
  - **CSS variables**: a `:root` block
  - **Tailwind config**: a v3 `tailwind.config.js`

### History

- The **History** page lists all your past scans, newest first. Hover over a scan to see its screenshot, and click it to open its results.
- The Generate page also shows your most recent scans.
- **Clear history** hides all your scans. They still count towards your usage, so clearing does not reset your limits.

### Roles and admin

| Role | Can do |
|---|---|
| **viewer** | Read and export scans |
| **member** *(default)* | Everything a viewer can, plus run scans and clear history |
| **admin** | Everything a member can, plus manage users |

The server stores roles and is the only thing that can change them. Emails listed in `Auth__AdminEmails__*` become admin when they sign in with a verified email. On **Admin > Users**, an admin can see every user and change their role. Admins cannot change their own role or remove the last admin.

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

Further reading:
- [backend/README.md](backend/README.md): every setting, the data model, the AI credit controls and the security notes
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): diagrams of the flow
- [docs/how-it-works/](docs/how-it-works/): step-by-step and visual guides
- [CHANGELOG.md](CHANGELOG.md): what has changed and why

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

```
MIT License

Copyright (c) 2026 KBOlifant

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
