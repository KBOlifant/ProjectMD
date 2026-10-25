---
name: project-md-hero-url-input
description: Use when changing the URL box, its Generate .MD button, URL validation, or the scan-submit behaviour.
---

# URL Input

Component: `frontend/components/ui/url-input.tsx` (`UrlInput`). Validation: `frontend/lib/url.ts` (`parseUrl`). Used by the Generate page (`../project-md-intake-page/SKILL.md`).

## Composition

One form: a labelled box holding a fixed `://` glyph and a text input, beside a primary "Generate .MD" button (both 52px tall; stacked on mobile, side by side from `sm`).

## Props

`value`, `onChange`, `onSubmit`, `error`, `disabled`, `notice`. It is a controlled form: typed text lives in the parent, and the component stores nothing.

## Behaviour

- The form is `noValidate` and the input is `type="text"` with `inputMode="url"`. It is deliberately not `type="url"`, because `parseUrl` accepts `example.com` and adds `https://`.
- Enter and the button both call `onSubmit` through the form's submit event.
- `parseUrl` adds `https://` when no scheme is given, accepts only http and https, and rejects hosts without a dot, `localhost`, and private ranges.
- The backend `UrlValidator` is the real boundary and is stricter (2048 characters maximum, no credentials in the URL, no non-public IPs). Keep `parseUrl` a friendly mirror of it; if you change one, check the other.
- `error` shows in `role="alert"` under the box with a red border. Without an error the line shows `notice`, or the default hint.
- `disabled` greys out the box and the button. Pair it with a `notice` that says why.

## States

- Default: `border-strong`, `bg-surface`
- Focus-within: `rose-400` border and `shadow-glow`
- Error: `danger` border
- Disabled: 60% opacity, `not-allowed` cursor

## Accessibility

- `aria-label="Website URL"`, `aria-invalid` on error, `aria-describedby` pointing at the hint or error line.
- The glyph is `aria-hidden`.

## Verification

- Test Enter, button click, an empty value, `localhost`, `ftp://x.com`, and `example.com`.
- Test keyboard focus and a screen reader reading the error.
- Check 375px: the button stacks below the box.
