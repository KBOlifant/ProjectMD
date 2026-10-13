---
name: workspace-auth-integration
description: Wire auth and RBAC into the signed-in app: user menu, role badge, verified-email banner, and permission gating on scan, history and results actions.
---

# Workspace auth integration

**Owns edits to:** `frontend/components/shell/*`, `frontend/app/(workspace)/page.tsx`, `frontend/app/(workspace)/history/page.tsx`, `frontend/app/(workspace)/processing/page.tsx`, `frontend/components/results/results-view.tsx`, `frontend/components/ui/url-input.tsx`, and new files under `frontend/components/auth-status/*`.
**Depends on:** 09, 10, 11, 12 all merged. Do not edit anything in `app/functions/*` (report needed changes instead).

## Build
- **User menu** in the sidebar footer above the usage meter (and a compact version in the mobile top bar): avatar tile with the email's initials, the email truncated, a role badge (`viewer` / `member` / `admin`, only show for non-member or always as a small chip), and a `Sign out` action (`signOutUser()` then `/login`). Keyboard accessible; a plain popover or inline button is fine, no new dependency.
- **Verify banner** (`components/auth-status/verify-banner.tsx`): shown at the top of the workspace content when `status === "authed"` and `!user.emailVerified`. Text "Verify your email to start scans." with a link to `/verify-email`. Uses `ts-*`/token classes, `role="status"`.
- **Generate screen:** disable the URL input and submit unless `useCan("scan:create")`. Show the reason from `useDenyReason("scan:create")`: `unverified` -> the banner link; `role` -> "Your account is read-only." Wrap `startScan` in try/catch for `PermissionError` and show it as the form error. Do not change copy for permitted users.
- **History:** the Clear history control needs `history:clear` (hide it otherwise).
- **Results:** Copy and Download need `scan:export` (disable with a title/explanation otherwise).
- **Processing:** if `session.error` is set (added by 11), show it in the card with a `Try again` that returns to `/`. If the page is opened with no session, keep existing behaviour.
- **Usage meter:** unchanged unless the user is `viewer`, then hide it.

## Rules
- Gate with `useCan`/`Can`; never duplicate role logic in components.
- Existing screens' visuals and copy stay as they are for permitted users.
- Token classes only; no new dependencies; short plain-English comment above each function.

## Done when
`npm run lint` and `npm run build` pass. With `NEXT_PUBLIC_DEV_ROLE=viewer` the create/clear controls are disabled or hidden; with `member` and an unverified email only scan creation is blocked, with the banner visible; sign-out returns to `/login`.
