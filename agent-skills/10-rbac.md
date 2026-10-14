---
name: rbac
description: Own Project MD's frontend role-based access control: roles, permissions, can/useCan/Can, and the verified-email rule.
---

# RBAC

> **Update:** the role is now stored in the database and set by the server (see [20-backend-roles.md](20-backend-roles.md)). The Firebase `role` claim is ignored by the server; in server mode the website takes the role from `/api/me` ([21](21-frontend-roles-admin.md)). The matrix below is unchanged.


**Owns:** `frontend/app/functions/rbac/*`.
**Depends on:** `useAuth` from `app/functions/auth/useAuth.ts` (written by 09 in parallel; code against the contract below, do not edit auth files).

## Roles
`viewer` < `member` < `admin`.
- The role comes from the Firebase custom claim `role` on the ID token. Claims are set only server-side (Admin SDK / backend), never from the client.
- `resolveRole(claim: unknown): Role`: missing claim (`undefined`/`null`) -> `member`; a valid string -> that role; any other value -> `viewer` (fail closed).
- Dev only: if `process.env.NODE_ENV !== "production"` and `NEXT_PUBLIC_DEV_ROLE` is a valid role, `resolveRole` returns it. Ignored in production builds.

## Permissions
```ts
type Permission = "scan:create" | "scan:read" | "scan:export" | "history:clear" | "admin:access";
```
| Role | Permissions |
|---|---|
| viewer | scan:read, scan:export |
| member | viewer + scan:create, history:clear |
| admin | member + admin:access |

Rule outside the matrix: `scan:create` also REQUIRES `emailVerified === true` for every role.

## Files
```
rbac/roles.ts        Role, ROLES, resolveRole(...)        // leaf module: imports nothing from the app
rbac/types.ts        CurrentUser { uid: string; email: string | null; emailVerified: boolean; role: Role }
rbac/permissions.ts  Permission, ROLE_PERMISSIONS (single const, easy to audit)
rbac/can.ts          can(user: CurrentUser | null, p): boolean;  denyReason(user, p): "signed-out" | "unverified" | "role" | null
rbac/errors.ts       class PermissionError extends Error { permission; reason }; assertCan(user, p) throws it
rbac/useCan.ts       "use client"; useCan(p): boolean (false while auth is loading); useDenyReason(p)
rbac/can-gate.tsx    <Can permission fallback?>children</Can>   // not Can.tsx: it would collide with can.ts on case-insensitive file systems
```
`roles.ts` and `can.ts` must stay pure (no React, no Firebase) so they can be unit-tested with a plain script. `useCan` reads `useAuth().user`.

## Rules
- Top-of-file comment in `permissions.ts`: this is a UX convenience and first line of defence only; the backend must enforce the same matrix from the verified ID token.
- Provide a runnable check: `frontend/app/functions/rbac/can.check.ts` (or a script under `frontend/scripts/`) that asserts the matrix, the unverified rule and the fail-closed role parsing, runnable with `npx tsx` if available, otherwise skip and say so. Do not add dependencies for this.
- Short plain-English comment above each function, matching the repo style.

## Done when
`npm run lint` passes, the matrix is covered by the check script (or its absence is reported), and the exports above exist with those exact names.
