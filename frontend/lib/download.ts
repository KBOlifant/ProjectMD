import type { Scan } from "@/lib/types";

// Saves text as a file through a temporary link, so the browser's Save dialog does the work.
// Nothing is stored in the app. Used by Results (Download), History (.MD and Export all).
export function downloadText(filename: string, text: string, mime = "text/markdown"): void {
  const blob = new Blob([text], { type: mime });
  const href = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = href;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(href);
}

// Downloads one scan's Markdown as <host>.md. Same text as the Raw tab shows.
export function downloadScan(scan: Pick<Scan, "host" | "markdown">): void {
  downloadText(`${scan.host}.md`, scan.markdown);
}

// Downloads every scan as one Markdown file, newest first, with a rule between documents.
export function downloadAllScans(scans: readonly Pick<Scan, "markdown">[]): void {
  downloadText("project-md-export.md", scans.map((s) => s.markdown.trim()).join("\n\n---\n\n") + "\n");
}
