---
name: app-shell
description: Build the Project MD app shell: sidebar rail, nav, usage meter, mobile top bar and page frame.
---

# App shell

**Owns:** `frontend/components/shell/*` and the `<body>` contents of `frontend/app/layout.tsx`.

## Read first
- Artifact `components/NavItem/README.md` and the README sections "Visual foundations" and "State".
- `frontend/node_modules/next/dist/docs/` guides for layouts and `Link` / `usePathname`.

## Build
- `Sidebar`: 240px rail on `bg-sidebar` with an `border-border-subtle` right edge. Brand at the top as plain type (no logo exists): `Project` in `ink`, `.MD` in `violet-400`, `ts-h3`.
- Group heading `WORKSPACE` (`ts-label`, `ink-dim`), then four `NavItem`s: Generate `/`, Processing `/processing`, Results `/results`, History `/history` (with a count badge).
- Footer `UsageMeter`: `USAGE` label, "N / LIMIT scans", a 3px track. It reads history length from the data layer.
- Below `md` the rail is replaced by a top bar with the brand and a horizontally scrolling nav row.
- `PageFrame`: content column with `space-8` gutters, max width 1040px.
- `layout.tsx` wraps children in `ScanProvider` and the shell. Set the metadata title to `Project MD`.

## Rules
- Active route uses `aria-current="page"`; match nested routes (`/results/abc` activates Results).
- Server component by default. Only the nav needs `"use client"` (for `usePathname`).
- No drop shadows on the rail; depth comes from the surface steps.

## Done when
All four routes show the shell, the active marker follows navigation, and the layout holds from 360px to 1440px.
