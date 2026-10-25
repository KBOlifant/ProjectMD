---
name: project-md-admin-users
description: Use when changing `/admin/users`, the users table, role changes, or the admin nav link.
---

# Admin Users Page

Route: `frontend/app/(workspace)/admin/users/page.tsx`. Components: `components/admin/users-panel.tsx` and `users-table.tsx`. Data: `app/functions/data/admin-api.ts`. Backend: `AdminController` (`GET /api/admin/users`, `PUT /api/admin/users/{id}/role`).

## Access

- The page is wrapped in `<Can permission="admin:access" fallback=...>`; everyone else sees "You don't have access to this page."
- The nav shows the Admin link only to those who pass `useCan("admin:access")`.
- Both are convenience. The backend policy `admin:access` is the real check. Never rely on the browser.
- Static mode has no backend, so the page explains it needs `NEXT_PUBLIC_SCAN_PROVIDER=api`.

## Behaviour

- `UsersPanel` states: loading skeleton, error with Retry, empty, table.
- Changing a role disables that row and shows "Saving…" until the server answers, then replaces the row with the returned user or shows a mapped error on that row.
- The signed-in admin's own row is disabled with an explanation (found by email). The backend also refuses it.
- Backend refusals map through `adminErrorMessage`: `self`, `last-admin`, 403, unknown role or user, server unreachable. Never show server text.
- A screen-reader-only live region announces each result.
- The user parser in `admin-api.ts` fails closed: an unknown role becomes `viewer`, and a row with no id is skipped.

## Rules

- Roles are `viewer`, `member`, `admin` only. The backend decides them; a role in a token claim is ignored in api mode.
- The first admins come from `Auth:AdminEmails` in backend configuration (`backend/.env`), not from the UI.
- The table scrolls sideways on narrow screens. Each role `<select>` has a label.

## Verification

- As a non-admin, open the URL directly: no data loads.
- Change a member to viewer and back; reload to confirm it stuck.
- Try to change your own role and to demote the last admin: both refuse with a clear message.
