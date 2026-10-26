"use client";

import { motion } from "framer-motion";
import { useState } from "react";
import { CopyIcon } from "@/components/ui/icons";
import { STAGGER_ITEM, STAGGER_LIST } from "@/lib/motion";
import type { AnimationItem, AnimationReport } from "@/lib/types";
import { TabHeader } from "./tab-header";
import { useCopy } from "./use-copy";

type Flavour = "framer" | "gsap" | "css";

const FLAVOURS: { id: Flavour; label: string; copy: string }[] = [
  { id: "framer", label: "Framer Motion", copy: "Copy Framer" },
  { id: "gsap", label: "GSAP", copy: "Copy GSAP" },
  { id: "css", label: "CSS", copy: "Copy CSS" },
];

// The code for one animation in the chosen flavour.
function codeFor(item: AnimationItem, flavour: Flavour): string {
  if (flavour === "gsap") return item.gsap;
  // Scans saved before the CSS option existed have no css field.
  if (flavour === "css") return item.css || "/* No CSS version for this scan. Run a new scan to get one. */";
  return item.framerMotion;
}

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

type PreviewKind = "blur" | "locomotive" | "cube";

// Picks a canned preview from whatever text the AI already gave us for this animation (its library
// hint, description, rebuild note) and the libraries measured on the page as a whole. No new data,
// just pattern-matching on what's already there. Falls back to the generic cube.
function previewKind(item: AnimationItem, libraries: readonly string[]): PreviewKind {
  const text = `${item.library} ${item.description} ${item.recreate}`.toLowerCase();
  const pageLibraries = libraries.join(" ").toLowerCase();
  if (text.includes("locomotive") || pageLibraries.includes("locomotive")) return "locomotive";
  if (text.includes("blur")) return "blur";
  return "cube";
}

// A small looping preview that stands in for the real captured animation: a blurred tile sharpening
// for "blur", three bars drifting up like an inertia-scrolled page for "locomotive", and a sliding,
// spinning square otherwise.
function Preview({ kind }: { kind: PreviewKind }) {
  if (kind === "blur") {
    return (
      <motion.div
        aria-hidden="true"
        className="size-10 rounded-md bg-violet-500"
        animate={{ filter: ["blur(0px)", "blur(10px)", "blur(0px)"] }}
        transition={{ duration: 2.4, repeat: Infinity, ease: "easeInOut" }}
      />
    );
  }
  if (kind === "locomotive") {
    return (
      <div aria-hidden="true" className="flex h-16 items-end gap-2 overflow-hidden">
        {[0, 1, 2].map((i) => (
          <motion.span
            key={i}
            className="w-3 rounded-sm bg-violet-500"
            style={{ height: 14 + i * 10 }}
            animate={{ y: [24, -24] }}
            transition={{ duration: 1.8, repeat: Infinity, ease: "easeInOut", delay: i * 0.2 }}
          />
        ))}
      </div>
    );
  }
  return (
    <motion.div
      aria-hidden="true"
      className="size-8 rounded-md bg-violet-500"
      animate={{ x: [-80, 80, -80], rotate: [0, 180, 360] }}
      transition={{ duration: 3, repeat: Infinity, ease: "easeInOut" }}
    />
  );
}

function Card({
  item,
  index,
  flavour,
  libraries,
  onCopy,
}: {
  item: AnimationItem;
  index: number;
  flavour: Flavour;
  libraries: readonly string[];
  onCopy: (text: string, label: string) => void;
}) {
  const code = codeFor(item, flavour);
  const meta = FLAVOURS.find((f) => f.id === flavour)!;
  return (
    <motion.li variants={STAGGER_ITEM} className="overflow-hidden rounded-xl border border-border-subtle bg-bg-surface">
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
      {/* Placeholder preview until real captured animations are rendered here; the kind is picked
          from the AI's own description, see previewKind. */}
      <div className="grid h-24 place-items-center border-b border-border-subtle bg-bg-base px-8">
        <Preview kind={previewKind(item, libraries)} />
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
    </motion.li>
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
      <motion.dl
        initial="initial"
        animate="animate"
        variants={STAGGER_LIST}
        className="grid overflow-hidden rounded-xl border border-border-subtle bg-bg-surface sm:grid-cols-3 sm:divide-x sm:divide-border-subtle"
      >
        {stats.map((s) => (
          <motion.div
            key={s.caption}
            variants={STAGGER_ITEM}
            className="border-b border-border-subtle px-5 py-4 last:border-b-0 sm:border-b-0"
          >
            <dd className="ts-stat truncate text-ink" title={s.value}>{s.value}</dd>
            <dt className="ts-small text-ink-muted">{s.caption}</dt>
          </motion.div>
        ))}
      </motion.dl>
      <p className="ts-small text-ink-muted">
        The library is a guess and the code is a starting point built from the page&apos;s typical timing. Replace <code>.target</code> with your own
        element and tune the values against the real site.
      </p>
      {report.items.length > 0 ? (
        <motion.ol initial="initial" animate="animate" variants={STAGGER_LIST} className="space-y-4">
          {report.items.map((item, i) => (
            <Card key={`${item.name}-${i}`} item={item} index={i} flavour={flavour} libraries={report.libraries} onCopy={copy} />
          ))}
        </motion.ol>
      ) : (
        <p className="ts-body text-ink-muted">No individual animations were described for this page.</p>
      )}
    </div>
  );
}
