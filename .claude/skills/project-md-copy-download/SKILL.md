---
name: project-md-copy-download
description: Use when changing clipboard copy, copied-state feedback, file downloads, export filenames, or who may export.
---

# Copy and Download

Two shared helpers. Use them; do not write clipboard or Blob code anywhere else.

- `copyText(text): Promise<boolean>` in `components/results/use-copy.ts`: the clipboard API, falling back to a hidden textarea and `execCommand("copy")` when it is blocked. True only when the copy happened.
- `downloadText(filename, text, mime)` in `lib/download.ts`: Blob, temporary link, click, revoke.

Where they are used:

| Where | Copy | Download |
| --- | --- | --- |
| `components/results/results-view.tsx` | `copyText(scan.markdown)`; the button label flips to "Copied" or "Copy blocked" for 1.5s | `<host>.md` or `<host>.txt` |
| `useCopy()` in `use-copy.ts` | `{ message, copy }` for the Design, Components and Animations panels, built on `copyText` | none |
| `components/results/design-export.tsx` | via `useCopy` | `design-tokens.json`, `design-tokens.css`, `tailwind.config.js` |

## Rules

- Report success only after the clipboard call resolves. On failure show a visible message ("Copy blocked" or "Copy blocked by the browser"). The status text goes in an `aria-live="polite"` region.
- The `useCopy` message clears itself after 1.8s and cleans up its timer on unmount. Keep that.
- Copy exactly what the visible export promises: the full Markdown for Copy and Download on the results page, the exact built text for design exports.
- Download: create a Blob with the right MIME type, click a temporary anchor, then `URL.revokeObjectURL`. Use meaningful filenames with the right extension, with the host in the name where there is one.
- Feedback sits next to the button that was pressed.
- Export controls need `scan:export` (`useCan`). When it is missing, disable them with a `title` and visible text "Your account cannot export scans." Also guard inside the handler, not just the button.

## Verification

- Test each output on its own, including repeated clicks (no stale "Copied").
- Block the clipboard in the browser and confirm the failure message or the fallback.
- Open the downloaded `.md`, `.txt`, `.json`, `.css` and `.js` files.
