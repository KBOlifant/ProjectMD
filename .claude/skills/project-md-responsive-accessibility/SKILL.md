---
name: project-md-responsive-accessibility
description: Use when changing responsive layouts, keyboard behaviour, focus, labels, live regions, reduced motion, or contrast.
---

# Responsive and Accessibility

## Layout

Tailwind 4 breakpoints are used in the components (there is no custom media-query CSS). The ones that matter:

- `sm`: the URL box and button sit side by side
- `md`: sidebar shows; below it the mobile top bar and horizontal nav row are used
- `md`: component previews use two columns and design export cards use three; `lg`: animation cards use two

The content column is `max-w-[1040px]` with `px-4 md:px-8`.

## Responsive priorities

1. Keep the primary action reachable.
2. Keep text and code readable. Code blocks, tabs and tables scroll inside their own box; they never widen the page.
3. Long hosts and tech lists wrap or truncate with `min-w-0`; nothing overlaps.

## Accessibility already in place (do not regress)

- One `h1` per page, and sections labelled with `aria-labelledby`.
- Results tabs use the full ARIA tab pattern with arrow keys (`../project-md-results-tabs/SKILL.md`).
- Nav items use `aria-current="page"`; the mobile account menu is a disclosure with `aria-expanded`, closes on Escape, and returns focus.
- Errors use `role="alert"`; copy and save feedback use `aria-live="polite"` or `role="status"`.
- Progress bars are `role="progressbar"` with values (`ProgressCard`, `UsageMeter`).
- Auth fields have labels, `aria-invalid` and `aria-describedby`.
- Global `:focus-visible` outline and a `prefers-reduced-motion` rule are in `globals.css`.
- Colour is never the only cue (active tab underline, icons plus text for status).

## Rules

- Use real buttons and links; do not put click handlers on `div`s.
- Disabled controls that matter explain why (`title` plus visible text).
- Preview iframes have a `title`.

## Verification

- Keyboard through nav, URL box, tabs, copy, export and the account menu.
- Test 375px, 768px and desktop, and zoom to 200%.
- Check long domains, long code lines and a list of 20 chips.
- Run a screen reader pass on the results page and the verify-email page.
