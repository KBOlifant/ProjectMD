---
name: frontend-roles-admin
description: Make the website take the user's role from the server (in server mode) and add a small admin Users page to change roles.
---

# Frontend roles from the server + admin Users page

**Owns:** `frontend/app/functions/auth/AuthProvider.tsx` (role source only), `frontend/app/functions/data/me-store.ts`, `data/usage-store.ts` (share the /api/me call), `data/admin-api.ts`, `frontend/app/(workspace)/admin/users/page.tsx`, `frontend/components/admin/*`, `frontend/components/shell/main-nav.tsx` and `shell.tsx` (admin link only), `frontend/app/functions/rbac/*` only if a new permission helper is truly needed.
**Contract (built by [20-backend-roles.md](20-backend-roles.md), code to it):** `GET /api/me` returns `{ uid, email, emailVerified, role, usage }` with the DATABASE role. `GET /api/admin/users` returns `AdminUser[]` `{ id, email, role, createdAt, lastSeenAt }`. `PUT /api/admin/users/{id}/role` body `{ role }` returns the updated user. Errors are ProblemDetails with `reason`: `role` (400 unknown role), `self` (400), `last-admin` (409), plus 403/404.

## Build
- **Role source.** Static mode (`NEXT_PUBLIC_SCAN_PROVIDER` not `api`): unchanged (token claim or `NEXT_PUBLIC_DEV_ROLE`). Server mode: the role comes ONLY from `GET /api/me`; ignore the token claim and the dev override. `me-store.ts` holds the single `/api/me` result (shared with the usage store so there is one request per sign-in/refresh, not two), is cleared on sign-out/user change, and drops stale responses.
- `AuthProvider` in server mode stays `loading` until `/api/me` has answered (guards must never see a half-built user). If `/api/me` fails, the user is signed in but with the safest role (`viewer`) and an `error` flag; the workspace shows a small "Can't reach the server" notice with Retry (reuse the verify-banner style). Never grant more than `viewer` on failure.
- `refresh()` also re-reads `/api/me`, so a role change shows up after the user reloads or refreshes.
- **Admin link.** In the nav (desktop and mobile) show `Admin` -> `/admin/users` only when `useCan("admin:access")`.
- **Admin Users page** (`/admin/users`): wrapped in `<Can permission="admin:access" fallback=...>` showing "You don't have access to this page." In static mode show "Managing users needs the server (set NEXT_PUBLIC_SCAN_PROVIDER=api)". Table with email, role (a `<select>` of viewer/member/admin), last seen, joined. Changing a role calls `PUT`, shows a per-row pending state, updates the row on success and shows the mapped message on failure (`self` -> "You can't change your own role.", `last-admin` -> "There must be at least one admin.", 403 -> "You don't have access.", network -> "Can't reach the server."). The signed-in admin's own row is disabled with an explanation. Loading skeleton, empty state, error state with Retry. Keyboard accessible, labelled selects, `aria-live` status.
- Use existing token classes and UI components (`Button`, `EmptyState`, `Tag`, `text-field`); no new dependencies.

## Rules
- Never put the token in logs/URLs. Short plain-English comment above each function. Static mode must keep working with no backend.
- Don't run git, don't sign in/up against the real Firebase project, don't edit `.env.local`. Test against a throwaway local fake server in the scratchpad directory.

## Done when
`npm run lint` and `npm run build` pass in both modes; with the fake server a user whose `/api/me` role is `admin` sees the Admin link and can change another user's role, while a `member` gets the no-access message and no link; a failing `/api/me` leaves a viewer with a Retry notice.
