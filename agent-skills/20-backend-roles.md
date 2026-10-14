---
name: backend-roles
description: Move user roles into the Project MD database: a Role column, an admin-email bootstrap list, per-request role lookup, and admin endpoints to list users and change roles.
---

# Backend roles in the database

**Owns:** `backend/ProjectMD.Api/Auth/*` (role source changes), `Models/AppUser.cs` (Role), `Controllers/AdminController.cs`, `Dtos/AdminUserDto.cs`, the new migration `AddUserRole`, tests under `ProjectMD.Api.Tests/Roles/*`, README section.
**Starts only after skills 18 and 19 are merged** (they also change the database model; two migrations at once would clash).
**Supersedes** the "role comes from the Firebase custom claim" rule in [10-rbac.md](10-rbac.md) and [15-backend-auth.md](15-backend-auth.md). The permission matrix is unchanged.

## Rules
- The role lives in `AppUser.Role` (`viewer` | `member` | `admin`, column max 20, default `member`). The server decides it. A `role` claim in the Firebase token is IGNORED (a user must never be able to raise their own role). Unknown or corrupt stored values fail closed to `viewer`.
- **Bootstrap admins:** config list `Auth:AdminEmails` (string array, set in user-secrets, never in tracked files; `appsettings.json` has an empty array). When a request arrives with `email_verified == true` and an email in that list (case-insensitive, trimmed), the user is promoted to `admin` in the database. Never promote on an unverified email. Promotion only; removing someone from the list does not demote them (the admin screen does that).
- `Auth:DefaultRole` (default `member`) is the role given to new users.
- **Per-request lookup:** a `DbRoleClaimsTransformation : IClaimsTransformation` runs after the JWT is validated, finds or creates the `AppUser` (reusing `IUserSync`), and sets the single `role` claim from the database. Cache the role per uid for 30 seconds (`IMemoryCache`), evict it when a role changes. Update `LastSeenAt` at most once a minute per user, not on every request. If the database is unavailable, fail the request with a 503 ProblemDetails; never fall back to a default role.
- `ICurrentUser.Role`, the policies and `/api/me` keep working, now fed by the database role.

## Admin endpoints (`[Authorize(Policy = Permissions.AdminAccess)]`)
- `GET /api/admin/users`: up to 200 users, newest first: `AdminUserDto { id, email, role, createdAt, lastSeenAt }`.
- `PUT /api/admin/users/{id:guid}/role` body `{ "role": "viewer|member|admin" }`: returns the updated `AdminUserDto`. Errors (ProblemDetails with `reason`): unknown role -> 400 `role`; changing your own role -> 400 `self`; removing the last admin -> 409 `last-admin`; user not found -> 404. Do the last-admin check and the update in one transaction. Evict the cache entry for that user.
- Log role changes (who, whom, from, to) at Information level, no emails beyond the ids.

## Migration
Add `Role` to `AppUsers` with a default of `member`, generate `AddUserRole` with the dummy-connection-string recipe in backend/README.md. Do NOT apply it and never connect to the real database.

## Tests
Database role beats a conflicting token claim; a token claiming `admin` stays `member`; bootstrap promotes only verified emails in the list; unverified email in the list is not promoted; viewer/member get 403 on admin endpoints, admin gets 200; self-change refused; last-admin protected under two concurrent demotions; unknown role 400; cache is evicted after a change; DB failure gives 503 and never grants access. Use SQLite/in-memory and the existing test token helpers.

## Done when
`dotnet build` has 0 warnings and `dotnet test` passes (sequentially); README documents `Auth:AdminEmails`, `Auth:DefaultRole`, the admin endpoints and the error reasons, with no secrets.
