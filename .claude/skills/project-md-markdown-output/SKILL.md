---
name: project-md-markdown-output
description: Use when changing the generated Markdown document, the Preview renderer, the Raw view, or how the Markdown is split between tabs.
---

# Markdown Output

## Where it comes from

Real scans: the backend builds the whole document in `Services/Pipeline/MarkdownDocumentBuilder.cs` (with `Markdown*Section.cs` files for API, design, design breakdown and motion). Facts are written by code from what the scan captured; the AI only supplies short prose, and it is cleaned by `AiProseSanitizer` and escaped again by `MarkdownText.Inline`. Static mode: `app/functions/data/static/mock-markdown.ts`.

The Markdown is stored in `Scan.markdown`. It is the single source for Preview, Raw, Copy and Download.

## Preview

`components/results/markdown-preview.tsx` renders the subset Project MD emits as React elements: front matter (as a code block), headings, blockquotes, nested lists, tables, fenced code, rules, paragraphs, bold, italic, inline code and backslash escapes.

- Never use `dangerouslySetInnerHTML`. Every piece of text becomes a React text node.
- If the backend starts emitting a new Markdown construct, add it to the renderer and check what it does with a table cell or an escaped character.

## Tab split

`splitDesignSections` in `lib/design-sections.ts` does the split. The "## Design system" and "## Design breakdown" sections go to the Design tab fallback; everything else stays on Preview. Headings inside fenced code and the YAML front matter are never treated as section starts. If you rename a heading in the backend builder, change `DESIGN_HEADINGS` too.

## Raw

A `<pre>` with the full Markdown and horizontal scrolling. Never inject scan HTML.

## Export

Copy and Download use the full Markdown (see `../project-md-copy-download/SKILL.md`). The download is `<host>.md` or `<host>.txt`.

## Verification

- Compare Preview and Raw for a real scan: nothing is missing, nothing duplicated across Preview and Design.
- Test a document with a long code line, a table, and a heading inside a code fence.
- Run the backend tests for the builder (`MarkdownDocumentBuilderTests`) after changing a section.
