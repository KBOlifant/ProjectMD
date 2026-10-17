// Splits a scan's Markdown into the part shown on the Preview tab and the part shown on the Design
// tab. The Design tab gets the "## Design system" section (measured by the scanner) and the
// "## Design breakdown" section (written by the AI); everything else stays on Preview. A section
// runs until the next "## " heading or the "---" rule before the footer. Lines inside fenced code
// blocks and the YAML front matter at the top are never treated as headings. Pure: no state.

export const DESIGN_HEADINGS = ["## Design system", "## Design breakdown"] as const;

export interface SplitMarkdown {
  /** The document without the design sections (front matter and footer kept). */
  document: string;
  /** Only the design sections, or "" when the scan has none. */
  design: string;
}

export function splitDesignSections(markdown: string): SplitMarkdown {
  const lines = markdown.split("\n");
  const document: string[] = [];
  const design: string[] = [];
  let inFrontMatter = lines[0]?.trim() === "---";
  let inFence = false;
  let inDesign = false;

  lines.forEach((line, i) => {
    if (inFrontMatter) {
      document.push(line);
      if (i > 0 && line.trim() === "---") inFrontMatter = false;
      return;
    }

    if (line.startsWith("```")) {
      inFence = !inFence;
    } else if (!inFence) {
      if (line.startsWith("## ")) {
        inDesign = (DESIGN_HEADINGS as readonly string[]).includes(line.trim());
      } else if (line.trim() === "---") {
        inDesign = false;
      }
    }

    (inDesign ? design : document).push(line);
  });

  return {
    document: document.join("\n").replace(/\n{3,}/g, "\n\n"),
    design: design.join("\n").trim(),
  };
}
