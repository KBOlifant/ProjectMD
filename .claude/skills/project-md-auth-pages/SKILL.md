---
name: project-md-auth-pages
description: Use when changing `/login`, `/signup`, `/reset`, `/verify-email`, their forms, or the redirect rules between signed-out and signed-in screens.
---

# Auth Pages

Routes live in `frontend/app/(auth)/`; forms in `frontend/components/auth/`. The `(auth)` folder does not change the URLs.

## Layout

`app/(auth)/layout.tsx` wraps everything in `RedirectIfAuthed`: a centred column, the Project.MD brand, a decorative glow, no sidebar.

## Screens

- `/login`: `LoginForm`, optional social buttons from `getEnabledSocialProviders()` (empty today)
- `/signup`: `SignupForm`, then the user goes straight to `/verify-email`
- `/reset`: `ResetForm` (password reset email)
- `/verify-email`: `VerifyEmailPanel`: shows the address, Resend with a 60s countdown, "I've verified", Sign out; re-checks about every 5s while the tab is visible

## Rules

- Validation in `components/auth/validation.ts` is for friendliness only (`MIN_PASSWORD_LENGTH` is 8). Firebase and the backend still validate.
- Error text comes from `app/functions/auth/errors.ts`. Never show raw Firebase codes, and never reveal whether an email has an account on the reset screen.
- After sign-in, redirect with `safeNext(next)` (`app/functions/auth/redirect.ts`): same-site paths only, no `//`, no backslash, no control characters. Never read `?next=` any other way.
- An unverified user may stay on `/verify-email`; everywhere else they are sent on or held there. Scans need a verified email (see `../project-md-rbac-permissions/SKILL.md`).
- Show a skeleton while auth state is loading so no form or workspace flashes.
- Fields use `TextField` / `PasswordField` with real labels, `aria-invalid`, and errors tied by `aria-describedby`. Messages go through `FormMessage`.
- Add a sign-in provider only in `app/functions/auth/providers.ts` and `auth-service.ts`, as that file's header explains.

## Verification

- Sign up, land on `/verify-email`, resend, verify, and arrive at `/`.
- Visit `/history` signed out: you land on `/login?next=%2Fhistory`, and after login you return.
- Try `?next=https://evil.com` and `?next=//evil.com`: both go to `/`.
- Use the Firebase emulator (`NEXT_PUBLIC_USE_AUTH_EMULATOR=true`) for repeatable runs.
