# Project MD: how the app works

Everything runs in the browser today. There is no backend, and the scan is a mock
(`buildScan` in `frontend/lib/scan.ts`). Diagrams are Mermaid; open this file in a Markdown
preview with Mermaid support (VS Code: "Markdown Preview Mermaid Support" extension, or GitHub).

## 1. User flow (pages)

```mermaid
flowchart LR
  A["/ Generate<br/>paste URL"] -->|valid URL| B["/processing<br/>progress card"]
  A -->|invalid| A
  B -->|Cancel| A
  B -->|3s timer done| C["/results/[id]<br/>preview, copy, download"]
  A -->|Recent scans| C
  D["/history<br/>all scans, clear"] --> C
  A -.->|View all| D
  E["/results<br/>newest scan"] --> C
```

## 2. What happens when you press "Generate .MD"

```mermaid
sequenceDiagram
  actor U as User
  participant G as GeneratePage (app/page.tsx)
  participant V as parseUrl (lib/url.ts)
  participant S as ScanProvider (memory)
  participant P as ProcessingPage
  participant B as buildScan (lib/scan.ts, MOCK)
  participant H as history-store (localStorage)
  U->>G: types URL, submits
  G->>V: parseUrl(text)
  V-->>G: ok + normalised url, or error message
  G->>S: startScan(url)  (session: url, elapsed 0)
  G->>P: router.push("/processing")
  loop every 100ms
    S->>S: elapsed += time passed
    P->>S: reads elapsed, draws step states
  end
  S->>B: buildScan(url) at 3000ms
  B-->>S: Scan {id, url, host, tech, markdown, ...}
  S->>H: addScan(scan)  SAVED to localStorage
  S-->>P: scanId set, "View Results" enabled
  U->>P: clicks View Results
```

## 3. Component tree

```mermaid
flowchart TD
  L["RootLayout (app/layout.tsx)"] --> SP["ScanProvider<br/>in-memory session"]
  SP --> SH["Shell"]
  SH --> NAV["MainNav<br/>reads history count"]
  SH --> UM["UsageMeter<br/>reads history count"]
  SH --> PG{{"Current page"}}
  PG --> G["GeneratePage<br/>UrlInput, StatStrip, HistoryRow x3"]
  PG --> PR["ProcessingPage<br/>ProgressCard"]
  PG --> RV["ResultsView<br/>TechChip, ConfidenceBadge, MarkdownPreview"]
  PG --> HI["HistoryPage<br/>HistoryRow list"]
```

## 4. Where data lives

```mermaid
flowchart LR
  subgraph Memory["In memory (lost on refresh)"]
    SES["ScanProvider session<br/>url, elapsed, scanId"]
    UI["Page UI state<br/>typed URL, error, tab, format,<br/>copy status, confirm-clear"]
    CACHE["history-store cache"]
  end
  subgraph Disk["Browser localStorage (persists)"]
    LS["key: projectmd.history<br/>JSON array of Scan"]
  end
  SES -->|buildScan result| ADD["addScan()"]
  ADD --> CACHE
  ADD --> LS
  LS -->|read() once per page load| CACHE
  CACHE -->|useHistory()| VIEWS["Generate stats, History, Results,<br/>NavCount, UsageMeter"]
```

Saved `Scan` object (`lib/types.ts`): `id`, `url`, `host`, `createdAt`, `durationMs`,
`confidence`, `pages`, `tech[]` (name, highlighted), `markdown`.

## 5. History store (subscribe / notify)

```mermaid
flowchart LR
  A["addScan / clearHistory"] --> W["write(next)"]
  W --> C["update cache"]
  W --> L["localStorage.setItem"]
  W --> N["call every listener"]
  N --> R["useHistory() consumers re-render"]
```

## 6. Replacing the mock with the backend (`NEXT_PUBLIC_SCAN_PROVIDER=api`)

The switch is one variable, read once at build/dev-server start. Unset or `static` is everything
described above (mock scan, history in localStorage, no network). `api` talks to the ASP.NET Core
backend at `NEXT_PUBLIC_API_URL` (default `http://localhost:5080`) with the Firebase ID token as a
Bearer header. The backend re-checks the token, role, `email_verified` and the monthly quota; the
frontend checks are only for a friendlier UI. The backend's scan generator is still a mock; the
real fetch/render/fingerprint/LLM pipeline is a later backend job, and no API key ever reaches the
frontend.

```mermaid
sequenceDiagram
  participant S as ScanProvider (memory)
  participant P as apiScanProvider
  participant C as apiFetch (api-client.ts)
  participant F as Firebase Auth
  participant B as Backend /api
  participant H as history-store (cache only)
  S->>P: generate(url) when the 3s timer ends
  P->>C: POST /api/scans { url }
  C->>F: getIdToken()
  C->>B: Authorization: Bearer token
  B-->>C: 201 Scan, or ProblemDetails (400, 403 reason, 429 reason)
  Note over C,B: on 401 it retries once with getIdToken(true)
  C-->>P: Scan, or ApiError (status 0 = server unreachable)
  P-->>S: Scan, or Error with a ready-to-show message
  S->>H: addScan(scan) updates the cache only (server already saved it)
  S->>B: refreshUsage(): GET /api/me
```

```mermaid
flowchart LR
  subgraph Server["Backend (source of truth)"]
    DB["Scans table + monthly usage"]
  end
  subgraph Memory["In memory (lost on refresh)"]
    CACHE["history-store cache"]
    USE["usage-store: used / limit"]
  end
  AUTH["setHistoryOwner(uid) after sign-in"] -->|GET /api/scans| DB
  AUTH -->|GET /api/me| DB
  DB --> CACHE
  DB --> USE
  CLR["clearHistory()"] -->|DELETE /api/scans, then empty the cache| DB
  CACHE -->|useHistory / useIsHydrated| VIEWS["History, Results, Generate stats, NavCount"]
  USE -->|useUsage| UM["UsageMeter"]
```

Notes:
- `useIsHydrated()` stays false while the first list is loading, so pages never flash "No scans yet".
  If loading fails, `useHistoryStatus().error` holds the message and History offers a Retry button.
- Errors are mapped once in `apiErrorMessage` (`app/functions/data/api-client.ts`): 403 `unverified`,
  403 `role`, 429 `quota`, other 429, 400 (the server's own text) and "Can't reach the server".
- The first `GET /api/me` after sign-in also makes the backend create the user's row.
