---
name: screen-auth
description: Build the Project MD login, signup, reset-password and verify-email screens.
---

# Auth screens

**Owns:** `frontend/app/(auth)/**`, `frontend/components/auth/*`, `frontend/components/ui/text-field.tsx`.
**Depends on:** 09 (auth plumbing), 10 (rbac types), 00 and 02 (tokens, UI components).

## Read first
- Design system artifact in [README](README.md); if it is not reachable, take tokens from `app/globals.css` and patterns from `components/ui/*`.
- `frontend/AGENTS.md` and the Next.js docs in `node_modules/next/dist/docs/` for route groups, `useSearchParams` (needs Suspense) and forms.
- [09-auth-firebase.md](09-auth-firebase.md) for the exact function names. Import only from `@/app/functions/auth/*`, never `firebase/*`.

## Build
- `app/(auth)/layout.tsx`: wraps children in `RedirectIfAuthed`; centred column on the dark background, brand as in the shell (`Project` + violet `.MD`), no sidebar. A faint violet radial glow like the Generate hero is fine; decorative, `aria-hidden`.
- `/login`: email, password, `Sign in` button, links to `/signup` and `/reset`. On success go to `safeNext(searchParams.next)`.
- `/signup`: email, password, confirm password. Client validation: valid email, password >= 8 chars, passwords match. On success go to `/verify-email`.
- `/reset`: email, `Send reset link`. Always show "If an account exists for that email, a reset link is on its way." Link back to `/login`.
- `/verify-email`: signed-in only (if anon, redirect to `/login`; if already verified, to `/`). Shows the address, explains that scans unlock after verifying, `Resend email` with a 60s cooldown countdown, `I've verified` (calls `refresh()`, shows "Not verified yet" if still false), `Sign out`. While the tab is visible, call `refresh()` every ~5s and continue to `/` automatically once verified.
- Provider area: render buttons from the `providers.ts` registry for enabled non-password providers; today that is none, so render nothing. Do not add a GitHub button.
- `components/ui/text-field.tsx`: labelled input with hint/error slots, `aria-invalid`, `aria-describedby`; matches the existing `url-input` styling.
- `components/auth/*`: shared `AuthCard`, `AuthForm` pieces as needed.

## Rules
- Real `<form>`, visible labels, correct `autoComplete` (`email`, `current-password`, `new-password`), `type="email"`/`"password"`, errors in an `aria-live="polite"` region, submit disabled while pending. Password fields have a show/hide toggle.
- Show only messages from `authErrorMessage`; never raw Firebase text.
- Token classes only (`bg-bg-surface`, `text-ink-muted`, `ts-label`...). No raw hex, no new dependencies.
- Wrap `useSearchParams` consumers in `<Suspense>`.
- Short plain-English comment above each function, matching the repo style.
- Do NOT submit the forms against the real Firebase project to test (that would create real accounts and send real emails). Test rendering, validation and layout only.

## Done when
`npm run lint` and `npm run build` pass; all four pages render at 360px and 1440px; validation errors show inline; keyboard-only use works.
