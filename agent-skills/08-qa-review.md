---
name: qa-review
description: Review the finished Project MD frontend against the design system and report defects. Read-only.
---

# QA and design review

**Owns:** nothing. Report defects; do not fix them.

## Check
1. **Tokens**: grep for raw hex, `px` colour or radius values, and arbitrary Tailwind values outside `globals.css`. Each is a defect.
2. **Design rules** (artifact README "Usage rules"): one primary button per view; no `violet-500` text on `bg-base`; `ink-dim` only on hints; sentence-case buttons; no emoji or exclamation marks.
3. **Components vs previews**: open each `preview.html` next to the built component.
4. **Flows**: Generate -> Processing -> Results -> History, plus invalid URL, cancel mid-scan, reload on every route, unknown result id, storage blocked.
5. **Accessibility**: tab order, focus ring on every control, `aria-current`, progressbar semantics, tab arrows, labels on icon buttons.
6. **Responsive**: 360, 768, 1440px. No horizontal page scroll.
7. **Build health**: `npm run lint` and `npm run build` in `frontend/`.

## Report
A table of defects with file, line, what is wrong, and the rule it breaks, ordered by severity. State which checks you ran and which you could not.
