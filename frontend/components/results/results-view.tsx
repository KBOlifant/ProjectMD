"use client";

import { useId, useMemo, useRef, useState } from "react";
import { isApiMode } from "@/app/functions/data/mode";
import { useCan } from "@/app/functions/rbac/useCan";
import { Button, ButtonLink } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/empty-state";
import { CheckIcon, CopyIcon, DownloadIcon } from "@/components/ui/icons";
import { ConfidenceBadge, TechChip } from "@/components/ui/tech-chip";
import { splitDesignSections } from "@/lib/design-sections";
import { formatDate, formatSeconds, initials } from "@/lib/format";
import { useHistory, useHistoryStatus, useIsHydrated } from "@/lib/history-store";
import { AnimationsPanel, hasAnimationData } from "./animations-panel";
import { ComponentsPanel, hasComponentData } from "./components-panel";
import { DesignPanel, hasDesignData } from "./design-panel";
import { MarkdownPreview } from "./markdown-preview";

type Tab = "preview" | "design" | "components" | "animations" | "raw";

const TABS: readonly Tab[] = ["preview", "design", "components", "animations", "raw"];
const TAB_LABELS: Record<Tab, string> = {
  preview: "Preview",
  design: "Design",
  components: "Components",
  animations: "Animations",
  raw: "Raw",
};
type Format = "md" | "txt";

// Shows one scan's documentation: header, copy/download buttons, tech chips and Preview/Design/Raw
// tabs. The Design tab shows the "Design system" and "Design breakdown" sections of the document
// (fonts, colours, components); Preview shows the rest; Raw and Copy/Download use the whole document.
// Finds the scan in saved history by `id` (or the newest scan if no id). Only reads history;
// the tab, format and copy status are temporary UI state and are not stored. Copy and Download
// are disabled (with an explanation) for accounts without the "scan:export" permission.
export function ResultsView({ id }: { id?: string }) {
  const history = useHistory();
  const hydrated = useIsHydrated();
  const { error: loadError } = useHistoryStatus();
  const canExport = useCan("scan:export");
  const exportBlocked = "Your account cannot export scans.";
  const [tab, setTab] = useState<Tab>("preview");
  const [format, setFormat] = useState<Format>("md");
  const [copyState, setCopyState] = useState<"idle" | "copied" | "failed">("idle");
  const tabRefs = useRef<Record<Tab, HTMLButtonElement | null>>({ preview: null, design: null, components: null, animations: null, raw: null });
  const panelId = useId();

  const scan = hydrated ? (id ? history.find((s) => s.id === id) : history[0]) : undefined;
  const sections = useMemo(() => splitDesignSections(scan?.markdown ?? ""), [scan?.markdown]);

  if (!hydrated) return null;

  if (!scan) {
    return (
      <EmptyState action={<ButtonLink href="/history" variant="secondary">Open History</ButtonLink>}>
        {loadError ??
          (id
            ? isApiMode
              ? "That scan was not found."
              : "That scan was not found in this browser."
            : "No results yet. Generate a scan to see its documentation here.")}
      </EmptyState>
    );
  }

  // Copies the scan's markdown to the clipboard and flashes "Copied"/"Copy blocked" for 1.5s. Stores nothing.
  async function copy() {
    if (!canExport) return;
    try {
      await navigator.clipboard.writeText(scan!.markdown);
      setCopyState("copied");
    } catch {
      setCopyState("failed");
    }
    setTimeout(() => setCopyState("idle"), 1500);
  }

  // Downloads the markdown as <host>.md or <host>.txt (chosen format) via a temporary file link. Nothing is stored in the app.
  function download() {
    if (!canExport) return;
    const blob = new Blob([scan!.markdown], { type: format === "md" ? "text/markdown" : "text/plain" });
    const href = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = href;
    a.download = `${scan!.host}.${format}`;
    a.click();
    URL.revokeObjectURL(href);
  }

  // Lets the left/right arrow keys move between the tabs (wrapping round). Stores nothing.
  function onTabKey(e: React.KeyboardEvent) {
    if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return;
    const step = e.key === "ArrowRight" ? 1 : -1;
    const next = TABS[(TABS.indexOf(tab) + step + TABS.length) % TABS.length];
    setTab(next);
    tabRefs.current[next]?.focus();
  }

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-center gap-4">
        <span className="grid size-[38px] flex-none place-items-center rounded-md bg-fig-500 font-mono text-[11px] font-medium text-ink">
          {initials(scan.host)}
        </span>
        <div className="min-w-0 flex-1 basis-56">
          <h1 className="ts-h2 truncate text-ink">{scan.host} Documentation</h1>
          <p className="ts-small truncate text-ink-muted">
            {scan.url} · Scanned {formatDate(scan.createdAt)} · {formatSeconds(scan.durationMs)} · {scan.pages} pages
          </p>
        </div>
        <ConfidenceBadge value={scan.confidence} />
      </header>

      <div className="flex flex-wrap items-center gap-3">
        <label className="flex items-center gap-2">
          <span className="ts-label text-ink-dim">Format</span>
          <select
            value={format}
            onChange={(e) => setFormat(e.target.value as Format)}
            className="ts-mono-ui rounded-md border border-border-strong bg-bg-raised px-3 py-2.5 text-ink-body"
          >
            <option value="md">Markdown (.md)</option>
            <option value="txt">Plain text (.txt)</option>
          </select>
        </label>
        <Button
          variant="secondary"
          onClick={copy}
          disabled={!canExport}
          title={canExport ? undefined : exportBlocked}
          className="ml-auto"
        >
          {copyState === "copied" ? <CheckIcon /> : <CopyIcon />}
          {copyState === "copied" ? "Copied" : copyState === "failed" ? "Copy blocked" : "Copy"}
        </Button>
        <Button onClick={download} disabled={!canExport} title={canExport ? undefined : exportBlocked}>
          <DownloadIcon />
          Download
        </Button>
        {!canExport && (
          <p className="ts-small basis-full text-right text-ink-dim">{exportBlocked}</p>
        )}
      </div>

      <section aria-labelledby="fingerprint">
        <h2 id="fingerprint" className="ts-label mb-2 text-ink-dim">
          Tech fingerprint
        </h2>
        <div className="flex flex-wrap gap-2">
          {scan.tech.map((t) => (
            <TechChip key={t.name} name={t.name} highlighted={t.highlighted} />
          ))}
        </div>
      </section>

      <div>
        <div role="tablist" aria-label="Output view" onKeyDown={onTabKey} className="flex gap-1 border-b border-border-subtle">
          {TABS.map((t) => (
            <button
              key={t}
              ref={(el) => {
                tabRefs.current[t] = el;
              }}
              role="tab"
              id={`${panelId}-${t}`}
              aria-selected={tab === t}
              aria-controls={panelId}
              tabIndex={tab === t ? 0 : -1}
              onClick={() => setTab(t)}
              className={`ts-mono-ui -mb-px border-b-2 px-4 py-2.5 transition-colors ${
                tab === t ? "border-mulberry-500 text-ink" : "border-transparent text-ink-muted hover:text-ink"
              }`}
            >
              {TAB_LABELS[t]}
            </button>
          ))}
        </div>
        <div
          role="tabpanel"
          id={panelId}
          aria-labelledby={`${panelId}-${tab}`}
          className="rounded-b-lg border border-t-0 border-border-subtle bg-bg-surface p-6"
        >
          {tab === "preview" && <MarkdownPreview source={sections.document} />}
          {tab === "design" &&
            (hasDesignData(scan.analysis?.design) ? (
              <DesignPanel design={scan.analysis.design} canExport={canExport} />
            ) : sections.design ? (
              <>
                <p role="status" className="ts-small mb-4 rounded-md border border-border-strong bg-bg-raised p-3 text-warning">
                  This is a text-only view. This scan has no visual design data (colour swatches, font previews),
                  because it was made before that feature or by an older server. Run a new scan to see them.
                </p>
                <MarkdownPreview source={sections.design} />
              </>
            ) : (
              <p className="ts-body text-ink-muted">
                No design data for this scan. Scans made in AI mode include the colours, fonts and design tokens here.
              </p>
            ))}
          {tab === "components" &&
            (hasComponentData(scan.analysis?.components) ? (
              <ComponentsPanel components={scan.analysis.components} />
            ) : (
              <p className="ts-body text-ink-muted">
                No component previews for this scan. Scans made in AI mode include small HTML and CSS versions of the
                site&apos;s buttons, inputs, cards and navigation here; older scans do not have them.
              </p>
            ))}
          {tab === "animations" &&
            (hasAnimationData(scan.analysis?.animations) ? (
              <AnimationsPanel report={scan.analysis.animations} />
            ) : (
              <p className="ts-body text-ink-muted">
                No animation data for this scan. Scans made in AI mode with screenshots turned on list the page&apos;s animations here,
                with a GSAP or Framer Motion guess and a starter snippet; older scans do not have them.
              </p>
            ))}
          {tab === "raw" && <pre className="ts-code overflow-x-auto text-ink-body">{scan.markdown}</pre>}
        </div>
      </div>
    </div>
  );
}
