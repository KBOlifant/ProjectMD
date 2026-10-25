---
name: project-md-auth-session
description: Use when changing Firebase sign-in, the AuthProvider and useAuth state, route guards, the verify-email rule, or how the backend validates tokens.
---

# Auth and Session

Frontend: `frontend/app/functions/auth/` (`auth-service.ts`, `AuthProvider.tsx`, `useAuth.ts`, `guards.tsx`, `redirect.ts`, `errors.ts`, `providers.ts`) and `app/functions/firebase/client.ts`. Backend: `backend/ProjectMD.Api/Auth/`. Screens: `../project-md-auth-pages/SKILL.md`.

## Model

- Firebase only signs people in. Passwords never reach Project MD. All other data (users, roles, scans) is in the Project MD database.
- `useAuth()` returns `{ status: "loading" | "authed" | "anon", user, error, refresh }`. `user` is `{ uid, email, emailVerified, role }` or null.
- UI code calls `auth-service.ts` and never imports `firebase/*` directly. Nothing logs or stores passwords or tokens.
- Sign-up creates the account and sends the verification email. If the email fails to send, sign-up still succeeds and the verify screen offers a resend.

## Roles by mode

- **api mode:** the role comes only from `GET /api/me` (the database role). Until it answers, the user is a `viewer`. If it fails, `error` is true and `ServerNotice` shows. The token claim and `NEXT_PUBLIC_DEV_ROLE` are ignored.
- **static mode:** the role comes from the token's `role` claim via `resolveRole`, or `NEXT_PUBLIC_DEV_ROLE` outside production. A missing claim means `member`; anything invalid means `viewer` (fails closed).

## Guards are UX only

`RequireAuth` (workspace) and `RedirectIfAuthed` (auth screens) keep the wrong audience out and avoid flashes with a skeleton. They protect nothing alone. The backend must verify the token, role and `email_verified` on every call.

## Backend

- `ConfigureFirebaseJwtBearer`: validates the Firebase ID token against Firebase's public keys; no service account. Reads `Firebase:ProjectId`. Raw claim names are kept and the token is not kept in the authentication properties.
- `DbRoleClaimsTransformation`: replaces any token `role` claim with the database role (cached 30s per uid and evicted on a change), creates the user row on first use, and promotes a user whose verified email is in `Auth:AdminEmails`.
- `UserSync`: upserts the `AppUser` by Firebase uid; the unique index guards a double insert; `LastSeenAt` is refreshed at most every so often, not on every request.

## Rules

- Any new provider goes through `providers.ts` and `auth-service.ts` only.
- Never log tokens, emails in errors, or request bodies.
- The dev "Copy API token" button exists only outside production.
- `?next=` is read only through `safeNext`.

## Verification

- Backend: `AuthEndpointTests`, `AuthPolicyTests`, `UserSyncTests`.
- Sign out during a scan: the session and history owner reset; the next account sees nothing of the previous one.
- Stop the backend: the notice appears and the account is read-only until Retry succeeds.
