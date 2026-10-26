"use client";

import { CodeIcon } from "@/components/ui/icons";
import { downloadText } from "@/lib/download";
import { EXPORT_FORMATS } from "@/lib/design-export";
import type { DesignSystem } from "@/lib/types";

interface Props {
  design: DesignSystem;
  onCopy: (text: string, label: string) => void;
  /** False when the account may not export scans (the buttons are disabled with a reason). */
  canExport?: boolean;
}

const BLOCKED_REASON = "Your account cannot export scans.";

const BUTTON = "ts-mono-ui rounded-md border border-border-strong px-3 py-1.5 text-ink-body transition-colors hover:bg-bg-raised hover:text-ink disabled:cursor-not-allowed disabled:opacity-50";

// The Export section of the Design tab: the design system as a Figma tokens file, CSS variables or a
// Tailwind config, each to copy or download. Builds the text on click from the scan; stores nothing.
export function DesignExport({ design, onCopy, canExport = true }: Props) {
  return (
    <section aria-labelledby="design-export" className="basis-full rounded-2xl border border-border-subtle bg-bg-surface p-5">
      <h3 id="design-export" className="ts-h3 text-ink">Export design system</h3>
      <p className="ts-small mt-1 text-ink-muted">Use the captured tokens wherever your team works.</p>
      {!canExport && <p className="ts-small mt-2 text-warning">{BLOCKED_REASON}</p>}
      <ul className="mt-4 grid gap-3 md:grid-cols-3">
        {EXPORT_FORMATS.map((format) => (
          <li key={format.id} className="flex flex-col gap-4 rounded-lg border border-border-subtle bg-bg-base p-4">
            <span className="grid size-9 place-items-center rounded-md border border-plum-700 text-plum-300">
              <CodeIcon size={15} />
            </span>
            <div className="flex-1">
              <p className="ts-mono-ui font-semibold text-ink">{format.label}</p>
              <p className="ts-small mt-1 text-ink-muted">{format.description}</p>
            </div>
            <div className="flex flex-wrap gap-2">
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
                onClick={() => downloadText(format.filename, format.build(design), format.mime)}
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
