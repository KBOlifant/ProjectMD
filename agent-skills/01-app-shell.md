---
name: app-shell
description: Build the Project MD app shell: 72px icon rail, usage gauge, account menu, mobile top bar and the PageFrame helper.
---

# App shell

**Owns:** `frontend/components/shell/*`, `frontend/components/ui/nav-item.tsx`, the account menu in `components/auth-status/user-menu.tsx`, and the shell wiring in `app/(workspace)/layout.tsx`.

## Read first
- Wireframes v2, any screen: the left rail is the same on all of them.
- `frontend/node_modules/next/dist/docs/` guides for layouts and `Link` / `usePathname`.

## Build
- **Rail** (desktop, `md` and up): fixed, 72px wide, `bg-sidebar`, right hairline. From top: `BrandTile` ("MD", 38px, `violet-700`, links to `/`), then icon-only nav, then at the bottom the account avatar and the usage gauge.
- **Nav** (`MainNav` + `NavItem layout="rail"`): Generate (spark), Processing (scan), Results (file), History (history clock, plum count badge), plus Admin (people) for admins. Each tile is 44px, rounded `lg`. Active = `bg-raised` with a `border-strong` outline and a 3px `violet-500` marker on the rail's left edge. Every tile has an `aria-label` (History adds the count) and a hover/focus tooltip to the right.
- **Usage gauge** (`UsageMeter`): vertical. Used count on top, a 3px track that fills upward, the limit underneath. `role="progressbar"` with `aria-valuetext` ("4 of 50 scans used"). Hidden for read-only roles.
- **Account menu** (`UserMenuCompact placement="rail"`): the avatar opens a panel to the right with email, role and Sign out. It is a disclosure: Escape and outside click close it.
- **Mobile** (below `md`): the rail becomes a top bar with the brand tile, "Project MD", the account menu (`placement="top-bar"`) and a scrolling row of `NavItem layout="row"` (icon + text).
- **No page wrapper in the shell.** Generate and Results run edge to edge. Pages that want a reading column use `PageFrame` (left aligned, `px-16`, 960px list or 520px narrow) and `Eyebrow` (plum label above a heading).
- The server notice and verify banner still sit above the page.

## Rules
- Active route uses `aria-current="page"`; nested routes match (`/results/abc` activates Results).
- Server component by default. Only the nav, usage meter and account menu are client components.
- No decoration on the rail: no shadows, no gradients. Depth comes from the surface steps.

## Done when
All routes show the rail, the marker follows navigation, tooltips appear on hover and keyboard focus, and the layout holds from 360px to 1920px.
