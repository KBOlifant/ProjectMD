---
name: project-md-rbac-permissions
description: Use when adding or changing a role, a permission, a gated control, or the refusal messages (unverified, role, signed-out).
---

# Roles and Permissions

Frontend: `frontend/app/functions/rbac/` (`roles.ts`, `permissions.ts`, `can.ts`, `useCan.ts`, `can-gate.tsx`, `errors.ts`). Backend: `backend/ProjectMD.Api/Auth/` (`Roles.cs`, `Permissions.cs`, `PermissionRules.cs`, `PermissionHandler.cs`, `PermissionRequirement.cs`, `ProblemDetailsAuthorizationResultHandler.cs`).

## The matrix

| Role | Permissions |
| --- | --- |
| `viewer` | `scan:read`, `scan:export` |
| `member` | viewer plus `scan:create`, `history:clear` |
| `admin` | member plus `admin:access` |

`scan:create` additionally needs a verified email for every role.

The matrix exists twice, in `frontend/app/functions/rbac/permissions.ts` and `backend/.../Auth/Permissions.cs`. The backend copy is the one that counts. Change both in the same change, and update the tests.

## Deny reasons

`can.ts` (`denyReason`) and `PermissionRules.DenyReason` return the same answers, in this order:

1. No user: `signed-out` (frontend only)
2. Role lacks it: `role`. This is checked before the email so a viewer is told verifying will not help.
3. `scan:create` and email unverified: `unverified`
4. Otherwise null

Unknown role or permission is refused (fails closed). The backend sends the reason as `reason` in the 403 ProblemDetails body; the frontend maps it in `apiErrorMessage`.

## Using it in the UI

- Hide or disable: `useCan("scan:export")`, `<Can permission="..." fallback={...}>`. Both are false while auth is loading and when signed out, so gated UI never flashes open.
- Explain: `useDenyReason(permission)`. A null here while loading does not mean allowed. Gate on `useCan` first.
- Guard actions too: `assertCan(user, permission)` throws `PermissionError`; callers catch it and show its message. Disabling a button is not enough.
- `can-gate.tsx` is deliberately not named `Can.tsx`, because `can.ts` would collide on case-insensitive file systems. Import it as `@/app/functions/rbac/can-gate`.

## Backend enforcement

Each permission is also an authorization policy, used as `[Authorize(Policy = Permissions.X)]` on the controllers: scans (`read`, `create`, `history:clear`), `AdminController` (`admin:access`), `MeController` (any signed-in user). A refused call returns a ProblemDetails body from the custom result handler.

## Adding a permission

1. Add it to `Permissions.cs` (`All`, `ByRole`) and `permissions.ts`.
2. Decide which roles get it. Spell the list out; do not inherit.
3. Put `[Authorize(Policy = ...)]` on the endpoint and gate the UI.
4. Add cases to `AuthPolicyTests` and `RolesApiTests`.

## Verification

- Try each role with `NEXT_PUBLIC_DEV_ROLE` (static mode) or by changing a user's role in the admin page (api mode).
- Call a gated endpoint with a viewer token (Swagger in Development): expect 403 with `reason` `role`.
