---
name: project-md-navigation
description: Use when changing the app shell, the sidebar or mobile top bar, nav items, the history count, the usage meter, or the notices above the page.
---

# Navigation and Shell

Files: `frontend/components/shell/` (`shell.tsx`, `main-nav.tsx`, `usage-meter.tsx`, `server-notice.tsx`) and `components/ui/nav-item.tsx`. The shell is applied in `app/(workspace)/layout.tsx`, not in the root layout, so the auth screens have none.

## Structure

- Desktop (`md` and up): sticky full-height sidebar, 240px wide: brand link, "Workspace" label, `MainNav`, then `UserMenu` and `UsageMeter` at the bottom.
- Mobile: top bar with the brand, `UserMenuCompact`, and `MainNav row` (a horizontally scrolling row).
- Content: `ServerNotice`, `VerifyBanner`, then the page inside a `max-w-[1040px]` column.

## Destinations

Generate `/`, Processing `/processing`, Results `/results`, History `/history`, and Admin `/admin/users` for users with `admin:access` only.

## Behaviour

- Active item: `usePathname()`; `/` matches exactly, others by prefix. The item gets `aria-current="page"` and a left accent line, so colour is not the only cue.
- History shows a count from `useHistory().length`.
- The Admin link is hidden for non-admins. This is convenience only; the backend enforces access.
- `UsageMeter` shows `used / limit scans` from `useUsage()`, a dash while loading, a warning colour at the limit, and is hidden for read-only roles. See `../project-md-usage-quota/SKILL.md`.
- `ServerNotice` appears only when signed in and the backend could not give the role. The user is read-only until Retry works.
- `VerifyBanner` appears for unverified emails.

## Rules

- Keep the sidebar flat (`bg-bg-sidebar`, one border). Do not turn it into a floating pill or a top navigation without being asked.
- Every item has a text label; icons are decorative.
- New destinations are added to `ITEMS` in `main-nav.tsx`. Gate them with `useCan` if they are permission-based.

## Verification

- Visit every destination and confirm the highlight.
- Check as admin and as member: only admins see Admin.
- At 375px the row scrolls and nothing overlaps.
- Stop the backend in api mode and confirm the notice shows and Retry works.
