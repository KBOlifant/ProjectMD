"use client";

import { EXPORT_FORMATS, type ExportFormat } from "@/lib/design-export";
import { downloadText } from "@/lib/download";
import type { DesignSystem } from "@/lib/types";

interface Props {
  design: DesignSystem;
  onCopy: (text: string, label: string) => void;
  /** False when the account may not export scans (the buttons are disabled with a reason). */
  canExport?: boolean;
}

const BLOCKED_REASON = "Your account cannot export scans.";

// Saves one export format as its file. Nothing is stored in the app.
function download(format: ExportFormat, text: string) {
  downloadText(format.filename, text, format.mime);
}

const BUTTON = "ts-mono-ui rounded-md border border-border-strong px-3 py-1.5 text-ink-muted hover:text-ink disabled:cursor-not-allowed disabled:opacity-50";

// The Export section of the Design tab: the design system as a Figma tokens file, CSS variables or a
// Tailwind config, each to copy or download. Builds the text on click from the scan; stores nothing.
export function DesignExport({ design, onCopy, canExport = true }: Props) {
  return (
    <section aria-labelledby="design-export" className="space-y-3">
      <h3 id="design-export" className="ts-label text-ink-dim">Export</h3>
      {!canExport && <p className="ts-small text-warning">{BLOCKED_REASON}</p>}
      <ul className="grid gap-3 md:grid-cols-3">
        {EXPORT_FORMATS.map((format) => (
          <li key={format.id} className="space-y-3 rounded-lg border border-border-strong bg-bg-raised p-4">
            <div>
              <p className="ts-label text-ink">{format.label}</p>
              <p className="ts-small text-ink-muted">{format.description}</p>
            </div>
            <div className="flex gap-2">
              <button
                type="button"
                disabled={!canExport}
                title={canExport ? undefined : BLOCKED_REASON}
                onClick={() => onCopy(format.build(design), format.label)}
                className={BUTTON}
              >
                Copy
              </button>
              <button
                type="button"
                disabled={!canExport}
                title={canExport ? undefined : BLOCKED_REASON}
                onClick={() => download(format, format.build(design))}
                className={BUTTON}
              >
                Download {format.filename}
              </button>
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}
