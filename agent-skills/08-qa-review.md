---
name: qa-review
description: Review the finished Project MD frontend against the v2 wireframes and design tokens and report defects. Read-only.
---

# QA and design review

**Owns:** nothing. Report defects; do not fix them.

## Check
1. **Wireframes v2** (8 PNGs): compare each screen at 1440 and 390px: rail, hero, what-you'll-get, processing card, results panes, the three data tabs, history.
2. **Tokens (00):** only token classes, no raw hex; plum for actions, violet for brand; small text never in `violet-500` or `plum-500`.
3. **Accessibility:** contrast 4.5:1, visible focus ring, labels on icon-only buttons (the rail!), tabs with arrow keys, `role="alert"` on errors, no nested interactive elements, reduced motion.
4. **Behaviour:** URL validation, permission gating (`scan:create`, `scan:export`, `history:clear`), static and api modes, empty/loading/error states, Copy/Download/Export all produce the right text.
5. **Known placeholders:** `public/hero.jpg` is supplied later; with it missing the hero must still look intentional. Results thumbnail is an illustration by design. Do not report these as defects.
6. **Honesty:** no invented numbers. Scan details show only real fields.
7. Run `npm run lint` and `npm run build` and quote their results; do not claim checks you did not run.

## Report
A table: severity (blocker, major, minor), screen, what is wrong, where (file), expected per wireframe or token.
