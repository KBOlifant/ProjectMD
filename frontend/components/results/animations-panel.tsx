"use client";

import { useState } from "react";
import type { AnimationItem, AnimationReport } from "@/lib/types";
import { useCopy } from "./use-copy";

type Flavour = "gsap" | "framer";

const TRIGGER_LABELS: Record<string, string> = {
  load: "Page load",
  scroll: "Scrolling into view",
  "scroll-scrub": "Follows the scrollbar",
  hover: "Hover",
  click: "Click",
  loop: "Continuous loop",
  "page-transition": "Page change",
  unknown: "Unknown trigger",
};

// True when a scan has animations or measured animation libraries worth showing.
export function hasAnimationData(report: AnimationReport | null | undefined): report is AnimationReport {
  return Boolean(report && (report.items?.length > 0 || report.libraries?.length > 0));
}

function Card({ item, onCopy }: { item: AnimationItem; onCopy: (text: string, label: string) => void }) {
  const [flavour, setFlavour] = useState<Flavour>("gsap");
  const code = flavour === "gsap" ? item.gsap : item.framerMotion;
  const label = flavour === "gsap" ? "GSAP" : "Framer Motion";
  return (
    <li className="space-y-3 rounded-lg border border-border-strong bg-bg-raised p-4">
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <h4 className="ts-label text-ink">{item.name || "Animation"}</h4>
        {item.section && <span className="ts-small text-ink-dim">{item.section}</span>}
      </div>
      <div className="flex flex-wrap gap-2 ts-mono-ui">
        <span className="rounded-md border border-border-strong px-2 py-0.5 text-ink-muted">{TRIGGER_LABELS[item.trigger] ?? TRIGGER_LABELS.unknown}</span>
        <span className="rounded-md border border-border-strong px-2 py-0.5 text-ink-muted">
          {item.evidence === "measured" ? "Measured" : "Inferred from screenshots"}
        </span>
        <span className="rounded-md bg-violet-700 px-2 py-0.5 text-ink">Looks like {item.estimate}</span>
      </div>
      {item.description && <p className="ts-body text-ink-body">{item.description}</p>}
      {item.recreate && <p className="ts-small text-ink-muted">How to rebuild: {item.recreate}</p>}
      <div className="space-y-2">
        <div role="group" aria-label={`Code library for ${item.name}`} className="flex gap-2">
          {(["gsap", "framer"] as const).map((f) => (
            <button
              key={f}
              type="button"
              aria-pressed={flavour === f}
              onClick={() => setFlavour(f)}
              className={`ts-mono-ui rounded-md border px-3 py-1 ${flavour === f ? "border-violet-500 text-ink" : "border-border-strong text-ink-muted hover:text-ink"}`}
            >
              {f === "gsap" ? "GSAP" : "Framer Motion"}
            </button>
          ))}
          <button
            type="button"
            onClick={() => onCopy(code, `${label} code`)}
            className="ts-mono-ui ml-auto rounded-md bg-violet-700 px-3 py-1 text-ink hover:bg-violet-600"
          >
            Copy code
          </button>
        </div>
        <pre className="ts-code overflow-x-auto rounded-md border border-border-subtle bg-bg-surface p-3 text-ink-body">{code}</pre>
      </div>
    </li>
  );
}

// The Animations tab: the animations found on the page, each with its trigger, how sure we are, a
// guess at GSAP or Framer Motion, and a starter snippet for either. Only reads the scan.
export function AnimationsPanel({ report }: { report: AnimationReport }) {
  const { message, copy } = useCopy();
  return (
    <div className="space-y-6">
      <p role="status" aria-live="polite" className="ts-mono-ui min-h-5 text-success">{message}</p>
      {report.summary && <p className="ts-body text-ink-body">{report.summary}</p>}
      {report.libraries.length > 0 && (
        <p className="ts-small text-ink-muted">
          Animation libraries found on the page: <span className="text-ink">{report.libraries.join(", ")}</span>
        </p>
      )}
      <p className="ts-small text-ink-muted">
        The library is a guess and the code is a starting point built from the page&apos;s typical timing. Replace <code>.target</code> with your own
        element and tune the values against the real site.
      </p>
      {report.items.length > 0 ? (
        <ul className="grid gap-4 lg:grid-cols-2">
          {report.items.map((item, i) => (
            <Card key={`${item.name}-${i}`} item={item} onCopy={copy} />
          ))}
        </ul>
      ) : (
        <p className="ts-body text-ink-muted">No individual animations were described for this page.</p>
      )}
    </div>
  );
}
