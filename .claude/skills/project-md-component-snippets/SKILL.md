---
name: project-md-component-snippets
description: Use when changing the Components tab, the sandboxed previews, the component style capture, or the AI step that writes the preview HTML and CSS.
---

# Component Previews

UI: `frontend/components/results/components-panel.tsx`. Types: `ComponentPreviews` and `ComponentPreview` (`kind`, `name`, `html`, `css`) in `lib/types.ts`. Stored in `Scan.analysis.components`.

## How the previews are made (current code)

1. **Capture** (`Services/Extraction/Components/`: `ComponentScript`, `ComponentCapture`, `ComponentSample`): the browser measures the computed styles of the page's main components (buttons, inputs, cards, navigation, and so on). Only style values are kept, no page text.
2. **AI** (`Services/Ai/ComponentAnalyzer.cs`, `ComponentPreviewsParser.cs`): in `ai` mode the text model writes small HTML and CSS versions from those measured values. It follows the same credit rules as the other AI stages: cache first, budget check before the call, one call, real token counts recorded. Nothing about the page text, URL or user is sent.
3. **Sanitise** (`Services/Ai/ComponentSanitizer.cs`): see below.

So the previews are approximate. A rebuild that creates them directly from the real element tree (so no AI is needed) was committed once (`85b628f`) and then reverted. `ComponentPreviewBuilder` does not exist in the code. `CHANGELOG.md` and `docs/HANDOFF.md` still describe the rebuild and contain unresolved merge-conflict markers; trust the code.

## Safety (two layers, keep both)

1. Server: `ComponentSanitizer` rebuilds the HTML from an allow-list of tags and attributes and the CSS from an allow-list of properties with plain values. No scripts, event handlers, inline styles, images, outside links, `url(...)` or `@` rules. The AI answer is untrusted.
2. Browser: each preview is an `<iframe sandbox="" referrerPolicy="no-referrer" srcDoc>` with the CSP `default-src 'none'; style-src 'unsafe-inline'`. `</` is broken up in the CSS so it cannot close the style tag.

## UI

- Two-column grid of frames, each with name, kind, and three buttons: Copy HTML, Copy CSS, Copy code (a `<style>` block then the HTML, through `componentCode`).
- A toggle switches the frame background between light (`#ffffff`) and dark (`#0b0b12`).
- A note tells the reader these are approximate and to check them against the real site.
- `hasComponentData` guards the tab; old scans and static-mode scans show a note.

## Rules

- Never render the `html` or `css` outside the sandboxed frame, and never loosen `sandbox` or the CSP without a security review.
- The preview and the copied code must show the same component.
- Copied code must not depend on Project MD.
- If the prompt, answer shape or sanitiser rules change, bump `ComponentAnalyzer.PromptVersion` so the cache does not serve old answers.

## Verification

- Run `ProjectMD.Api.Tests/Ai/ComponentPreviewTests.cs` after changing capture, parsing or sanitising.
- Add a hostile case (script tag, `onerror`, `url(http://...)`, `</style>`) and confirm it is removed.
- Scan a real site and compare a preview with the original in a browser.
