---
name: auth-firebase
description: Own Project MD's Firebase email/password auth plumbing: client init, auth service, AuthProvider, guards, env config and the route-group split.
---

# Auth (Firebase) plumbing

**Owns:** `frontend/app/functions/firebase/*`, `frontend/app/functions/auth/*`, `frontend/.env.example`, `frontend/app/layout.tsx`, the move of the four screens into `app/(workspace)/`, and `app/(workspace)/layout.tsx`.
**Does not own:** login/signup UI (12), `rbac/` (10), `data/` and `history-store` (11), `components/shell/*` contents (13).

## Read first
- `frontend/AGENTS.md` and `frontend/node_modules/next/dist/docs/` guides for layouts, route groups and `LayoutProps`.
- [10-rbac.md](10-rbac.md) and [11-data-provider.md](11-data-provider.md) for the contracts you import.

## Setup
- `npm install firebase` inside `frontend/` (the root `package.json` also lists it, but `frontend/` must own its dependency). Use the same major as the root (`^13`).
- Config comes from `frontend/.env.local` (already created by the user, gitignored): `NEXT_PUBLIC_FIREBASE_API_KEY`, `_AUTH_DOMAIN`, `_PROJECT_ID`, `_STORAGE_BUCKET`, `_MESSAGING_SENDER_ID`, `_APP_ID`. Create `frontend/.env.example` with the same keys and empty values plus `NEXT_PUBLIC_USE_AUTH_EMULATOR=false` and a commented `NEXT_PUBLIC_DEV_ROLE=`. Never print, log or copy the real values anywhere else. No Analytics SDK.
- `firebase/client.ts`: `getFirebaseAuth()` initialises lazily and once (`getApps()` guard). Importing the module must not throw during SSR or `next build`; throw one clear error naming the missing variable only when `getFirebaseAuth()` is called in the browser. When `NEXT_PUBLIC_USE_AUTH_EMULATOR === "true"` call `connectAuthEmulator(auth, "http://127.0.0.1:9099")` once.

## Build
```
app/functions/
  firebase/client.ts
  auth/
    auth-service.ts   signUp(email, password), signIn(email, password), signOutUser(),
                      sendVerificationEmail(), sendPasswordReset(email), reloadUser(),
                      getIdToken(forceRefresh?), getCurrentUserId(),
                      signInWithProvider(id)  // throws for disabled/unknown ids
    providers.ts      registry: { id:"password", enabled:true }, { id:"github", enabled:false }
    errors.ts         authErrorMessage(error, context: "login"|"signup"|"reset"|"verify"): string
    redirect.ts       safeNext(next: string | null | undefined): string
    AuthProvider.tsx  "use client" context
    useAuth.ts
    guards.tsx        RequireAuth, RedirectIfAuthed
```
- `AuthProvider` value: `{ status: "loading" | "authed" | "anon"; user: CurrentUser | null; refresh(): Promise<void> }` where `CurrentUser` is imported from `functions/rbac/types.ts`. Subscribe with `onAuthStateChanged`. On each change read `getIdTokenResult()`, build `CurrentUser` with `role: resolveRole(claims.role)` (from `rbac/roles.ts`) and `emailVerified`. Call `setHistoryOwner(user?.uid ?? null)` from `@/lib/history-store` (contract in 11) so each account has its own history. `refresh()` = `reloadUser()` then `getIdToken(true)` then rebuild the user.
- `signUp` creates the user, then calls `sendEmailVerification`. No continue URL. If the verification email fails, still resolve the signup and let the verify page offer a resend.
- `errors.ts` rules: login and reset never reveal whether an email exists. `auth/invalid-credential`, `auth/user-not-found`, `auth/wrong-password` -> "Incorrect email or password." Signup only: `auth/email-already-in-use` -> "An account with this email already exists. Sign in instead." Also `auth/weak-password`, `auth/invalid-email`, `auth/too-many-requests` ("Too many attempts. Try again later."), `auth/network-request-failed` ("Check your connection and try again."), default "Something went wrong. Try again." For reset, show the same success text whether or not the account exists, and swallow `auth/user-not-found`.
- `safeNext`: only a single leading `/` (not `//`, no backslash, no scheme); otherwise `/`.
- `RequireAuth`: `loading` renders a neutral full-screen skeleton (no flash of content); `anon` does `router.replace("/login?next=" + encodeURIComponent(path))`. `RedirectIfAuthed`: authed -> `router.replace(safeNext(next))`; authed but unverified is allowed on `/verify-email` only; everyone else goes to `/`.
- GitHub later: the `providers.ts` registry is the only place that changes (add `GithubAuthProvider` + `signInWithPopup`, handle `auth/account-exists-with-different-credential`). Do not build the GitHub button.

## Route groups (URLs unchanged)
```
app/layout.tsx                 fonts + <AuthProvider><ScanProvider>{children}</ScanProvider></AuthProvider>; no Shell
app/(auth)/layout.tsx          created by 12
app/(workspace)/layout.tsx     <RequireAuth><Shell>{children}</Shell></RequireAuth>
app/(workspace)/page.tsx, history/, processing/, results/   moved from app/ unchanged
```
Move with `Move-Item`; do not edit the screens' contents. `ScanProvider` must sit inside `AuthProvider` (11 makes it read the user).

## Rules
- Client checks are UX only. Comment where relevant that the backend must verify the Firebase ID token (`getIdToken()`), role and `email_verified` on every API call.
- Never log tokens, passwords or Firebase error objects.
- Only `functions/auth` and `functions/firebase` import `firebase/*`. UI imports from `@/app/functions/...`.
- Follow the repo's comment style: a short plain-English comment above each function saying what it does and what it stores.

## Done when
`npm run lint` and `npm run build` pass; signed-out visits to `/`, `/history`, `/processing`, `/results` redirect to `/login?next=...`; `/login` etc. are reachable (UI from 12 may still be placeholders); signing out clears the user. Report the exact exports other agents rely on.
