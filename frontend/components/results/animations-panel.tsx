"use client";

import { useState } from "react";
import { CopyIcon } from "@/components/ui/icons";
import type { AnimationItem, AnimationReport } from "@/lib/types";
import { TabHeader } from "./tab-header";
import { useCopy } from "./use-copy";

type Flavour = "framer" | "gsap";

const FLAVOURS: { id: Flavour; label: string; copy: string }[] = [
  { id: "framer", label: "Framer Motion", copy: "Copy Framer" },
  { id: "gsap", label: "GSAP", copy: "Copy GSAP" },
];

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

const CHIP = "rounded-sm border border-border-strong px-2 py-0.5 font-mono text-[10px] leading-4 text-ink-muted";

function Card({ item, index, flavour, onCopy }: { item: AnimationItem; index: number; flavour: Flavour; onCopy: (text: string, label: string) => void }) {
  const code = flavour === "gsap" ? item.gsap : item.framerMotion;
  const meta = FLAVOURS.find((f) => f.id === flavour)!;
  return (
    <li className="overflow-hidden rounded-xl border border-border-subtle bg-bg-surface">
      <div className="flex flex-wrap items-center gap-3 border-b border-border-subtle px-5 py-3.5">
        <span aria-hidden="true" className="font-mono text-[10px] text-plum-300">{String(index + 1).padStart(2, "0")}</span>
        <div className="min-w-0 flex-1 basis-40">
          <h3 className="ts-mono-ui truncate font-semibold text-ink">{item.name || "Animation"}</h3>
          <p className="font-mono text-[10px] leading-4 text-ink-muted">
            {[TRIGGER_LABELS[item.trigger] ?? TRIGGER_LABELS.unknown, item.section].filter(Boolean).join(" · ")}
          </p>
        </div>
        <button
          type="button"
          onClick={() => onCopy(code, `${meta.label} code`)}
          className="ts-mono-ui inline-flex items-center gap-1.5 rounded-md border border-border-strong px-3 py-1.5 text-ink-body transition-colors hover:bg-bg-raised hover:text-ink"
        >
          <CopyIcon size={13} />
          {meta.copy}
        </button>
      </div>
      <div className="space-y-3 px-5 py-4">
        <div className="flex flex-wrap gap-2">
          <span className={CHIP}>{item.evidence === "measured" ? "Measured" : "Inferred from screenshots"}</span>
          <span className={`${CHIP} border-violet-500/50 text-ink-body`}>Looks like {item.estimate}</span>
        </div>
        {item.description && <p className="ts-body text-ink-body">{item.description}</p>}
        {item.recreate && <p className="ts-small text-ink-muted">How to rebuild: {item.recreate}</p>}
      </div>
      <pre className="ts-code overflow-x-auto border-t border-border-subtle bg-bg-sidebar p-5 text-ink-body">{code}</pre>
    </li>
  );
}

// The Animations tab: a header with the Framer Motion / GSAP switch, three quick counts, then one
// card per animation found on the page with its trigger, how sure we are, a guess at the library and
// a starter snippet in the chosen library. Only reads the scan; the library choice is temporary UI state.
export function AnimationsPanel({ report }: { report: AnimationReport }) {
  const { message, copy } = useCopy();
  const [flavour, setFlavour] = useState<Flavour>("framer");
  const measured = report.items.filter((i) => i.evidence === "measured").length;
  const stats = [
    { value: String(report.items.length), caption: "Animations" },
    { value: String(measured), caption: "Measured on the page" },
    { value: report.libraries.length > 0 ? report.libraries.join(", ") : "None", caption: "Libraries found" },
  ];
  return (
    <div className="space-y-6">
      <TabHeader
        eyebrow="Motion inventory"
        title="Captured animations"
        description="Timing, easing, and movement translated into copy-ready animation code."
        aside={
          <div role="group" aria-label="Code library" className="inline-flex flex-none rounded-lg border border-border-strong bg-bg-surface p-1">
            {FLAVOURS.map((f) => (
              <button
                key={f.id}
                type="button"
                aria-pressed={flavour === f.id}
                onClick={() => setFlavour(f.id)}
                className={`rounded-md px-3 py-1.5 font-mono text-[10px] transition-colors ${
                  flavour === f.id ? "bg-bg-raised text-ink" : "text-ink-muted hover:text-ink"
                }`}
              >
                {f.label}
              </button>
            ))}
          </div>
        }
      />
      <p role="status" aria-live="polite" className="ts-mono-ui min-h-5 text-success">{message}</p>
      {report.summary && <p className="ts-body max-w-3xl text-ink-body">{report.summary}</p>}
      <dl className="grid overflow-hidden rounded-xl border border-border-subtle bg-bg-surface sm:grid-cols-3 sm:divide-x sm:divide-border-subtle">
        {stats.map((s) => (
          <div key={s.caption} className="border-b border-border-subtle px-5 py-4 last:border-b-0 sm:border-b-0">
            <dd className="ts-stat truncate text-ink" title={s.value}>{s.value}</dd>
            <dt className="ts-small text-ink-muted">{s.caption}</dt>
          </div>
        ))}
      </dl>
      <p className="ts-small text-ink-muted">
        The library is a guess and the code is a starting point built from the page&apos;s typical timing. Replace <code>.target</code> with your own
        element and tune the values against the real site.
      </p>
      {report.items.length > 0 ? (
        <ol className="space-y-4">
          {report.items.map((item, i) => (
            <Card key={`${item.name}-${i}`} item={item} index={i} flavour={flavour} onCopy={copy} />
          ))}
        </ol>
      ) : (
        <p className="ts-body text-ink-muted">No individual animations were described for this page.</p>
      )}
    </div>
  );
}
