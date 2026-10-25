---
name: project-md-intake-page
description: Use when changing the Generate page at `/`: the hero, URL submit flow, summary stats, or recent-scans list.
---

# Intake (Generate) Page

Route: `frontend/app/(workspace)/page.tsx` (`GeneratePage`). Signed-in users only; the `(workspace)` layout wraps it in `RequireAuth` and `Shell`.

## Purpose

One clear action: paste a public URL and start a scan.

## Owned UI

- Hero with the `hero-glow` utility, an "AI-Powered · Live" pill, the display heading, and a short description
- `UrlInput` (see `../project-md-hero-url-input/SKILL.md`)
- `StatStrip`: Total Scans, Avg Confidence, Pages Indexed, Avg Speed
- "Recent scans": the 3 newest `HistoryRow`s, with a "View all →" link only when there are more than 3

## Submit flow

1. `parseUrl(value)` (`lib/url.ts`). On failure, show its message as the form error and stay.
2. `startScan(url)` from `useScan()`. It calls `assertCan(user, "scan:create")` and can throw `PermissionError`; catch it and show `err.message` as the form error. Re-throw anything else.
3. `router.push("/processing")`.

Nothing is saved on this page. `ScanProvider` saves the finished scan.

## Rules

- Stats are computed from `useHistory()` on every render. Never store them.
- Typed value and error are local state only.
- Wait for `useIsHydrated()` before showing the empty state, or "No scans yet" flashes while history loads. Show `loadError` in the empty state if the history load failed.
- When `useCan("scan:create")` is false, disable the input and pass `<ScanBlockedNotice reason={useDenyReason(...)} />` as `notice`. Do not hide the box.
- Use token classes only (see `../project-md-visual-system/SKILL.md`). The hero glow is the one allowed gradient.

## Verification

- Submit with Enter and with the button; both must behave the same.
- Try an empty value, `localhost`, and a bad URL: each shows its own message.
- Try as a `viewer` (set `NEXT_PUBLIC_DEV_ROLE=viewer`) and as an unverified account: the box is disabled and the reason is shown.
- Check the page at 375px and a desktop width.
