---
name: backend-auth
description: Validate Firebase ID tokens in ASP.NET Core and enforce the same RBAC matrix as the frontend with authorization policies; add the /api/me endpoint.
---

# Backend auth and RBAC

> **Update:** the role is now stored in the database and set by the server (see [20-backend-roles.md](20-backend-roles.md)). The Firebase `role` claim is ignored by the server; in server mode the website takes the role from `/api/me` ([21](21-frontend-roles-admin.md)). The matrix below is unchanged.


**Owns:** `backend/ProjectMD.Api/Auth/*`, `Controllers/MeController.cs`, and the marked authentication/authorization block in `Program.cs`.
**Depends on:** 14. **Mirrors:** [10-rbac.md](10-rbac.md) (the frontend matrix). The backend is the real enforcement; the frontend is UX only.

## Token validation (no service account needed)
- JWT bearer, `MapInboundClaims = false`.
- Authority `https://securetoken.google.com/{Firebase:ProjectId}`, valid issuer the same URL, valid audience = the project id, validate lifetime (60s clock skew max), require signed tokens. Keys are fetched from the authority's OIDC metadata.
- Read claims: `sub` or `user_id` -> Firebase uid, `email`, `email_verified` (bool), `role` (string, custom claim).
- Never log tokens or the `Authorization` header.

## Roles and permissions (same as frontend)
- `Roles`: `viewer` < `member` < `admin`. `ResolveRole(claim)`: claim absent -> `member`; a valid value -> that role; anything else -> `viewer` (fail closed).
- Permissions as policy names, exactly these strings: `scan:create`, `scan:read`, `scan:export`, `history:clear`, `admin:access`.

| Role | Permissions |
|---|---|
| viewer | scan:read, scan:export |
| member | viewer + scan:create, history:clear |
| admin | member + admin:access |

- `scan:create` ALSO requires `email_verified == true` for every role.
- Implement with a `PermissionRequirement(string permission)` + one `AuthorizationHandler`, register one policy per permission via `AddAuthorization`, and a fallback policy that requires an authenticated user. Expose a constant class `Permissions` for use in `[Authorize(Policy = Permissions.ScanCreate)]`.
- A failed policy returns 403 ProblemDetails, a missing/invalid token 401. For `scan:create`, put a machine-readable `reason` extension in the 403 body: `"unverified"` (email not verified) or `"role"`, so the frontend can show the right message.
- `ICurrentUser` service (scoped): `Uid`, `Email`, `EmailVerified`, `Role`, built from `HttpContext.User`.

## `MeController` (`[Authorize]`)
- `GET /api/me`: upsert the `AppUser` row by `FirebaseUid` (set `Email`, `LastSeenAt = UtcNow`; create if missing, tolerating a concurrent-insert race with a unique-index retry), then return `MeDto { uid, email, emailVerified, role, usage }` where usage is computed by `IUsageService` (owned by 16; code against `Task<UsageDto> GetAsync(Guid userId, CancellationToken)`; if it doesn't exist yet when you build, create a minimal `Services/IUsageService.cs` interface only and report it).
- Provide an `IUserSync` helper (`GetOrCreateAsync(ICurrentUser)`) so 16's controller can reuse the same upsert.

## Rules
- Unit tests live with 16 in the test project; you add `AuthPolicyTests` there only for the matrix (`viewer` cannot create, `member` unverified cannot create, unknown role fails closed, admin has all, missing claim = member). If the test project doesn't exist yet, create `backend/ProjectMD.Api.Tests` (xUnit) and tell 16.
- No Firebase Admin SDK, no credentials. Assigning roles is a later task (needs a service account); leave a `// TODO(roles)` note in the README, not code.
- Short plain-English comment above each class/method.

## Done when
`dotnet build` and `dotnet test` pass; without a token every `/api/*` route except `/api/health` returns 401; the matrix tests pass.
