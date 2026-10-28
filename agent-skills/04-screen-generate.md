---
name: screen-generate
description: Build the Project MD Generate screen (wireframes v2): full-height hero with URL bar, "What you'll get" panel, stat strip and recent scans.
---

# Screen: Generate (`/`)

**Owns:** `frontend/app/(workspace)/page.tsx`, `components/ui/hero-backdrop.tsx`.

## Read first
Wireframes v2: "Hero / Generate" and "What you'll get". Keep the scan logic of the existing page (parseUrl, startScan, `PermissionError` shown as the form error, hydration guard).

## Build
1. **Hero** (full viewport height on desktop, min 640px): `HeroBackdrop` behind everything, centred column 700px wide:
   - pill "AI website intelligence" with a violet dot;
   - `ts-hero` "Project" over "MD" (MD in `violet-400`);
   - one 17px paragraph: inspect the stack, content, components, design tokens and motion, then package it for the team;
   - `UrlInput` (violet **Analyze website** button, hint row). Disabled with `ScanBlockedNotice` when the account cannot create scans.
2. **Hero image is a placeholder.** `HeroBackdrop` draws `/hero.jpg` over a dark violet gradient and a 35% scrim. Until the real image is supplied, drop it at `frontend/public/hero.jpg` (landscape, about 2400px wide, dark enough for white text). No code change is needed; a missing file falls back to the gradient.
3. **What you'll get**: `Eyebrow`, h2 "A complete handoff, not another audit.", then three numbered cells in one bordered panel (Technical documentation, Design system exports, Reusable building blocks).
4. **Stats and recent scans**: `StatStrip` (Total scans, Avg confidence, Pages indexed, Avg speed, all computed from history) and the latest three `HistoryRow`s with `.MD` download (gated by `scan:export`) plus "View all" when more than three. Empty state when none. These two blocks are not in the wireframe; they stay because they are cheap and useful, and are safe to delete.

## Rules
- One `h1`. No second primary button. Respect `prefers-reduced-motion` (no animation is required).
- The hero text must stay readable over any photo: keep the scrim.

## Done when
Submitting a valid URL goes to `/processing`; an invalid one shows the error under the bar; the page matches the wireframe at 1440 and 390px wide.
