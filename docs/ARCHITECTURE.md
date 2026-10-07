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

## 6. Planned: replacing the mock with a real backend

```mermaid
flowchart LR
  I["ScanProvider"] -->|await| API["POST /api/scan (to build)"]
  API --> F["Fetch URL"] --> R["Render JS"] --> T["Fingerprint stack"] --> M["LLM writes Markdown"]
  M --> API -->|Scan JSON| I
  I --> H["addScan (unchanged)"]
```
