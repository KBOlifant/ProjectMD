"use client";

import { useId, useMemo, useRef, useState, type ReactNode } from "react";
import { isApiMode } from "@/app/functions/data/mode";
import { useCan } from "@/app/functions/rbac/useCan";
import { PageFrame } from "@/components/shell/page-frame";
import { Button, ButtonLink } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/empty-state";
import { CheckIcon, CopyIcon, DownloadIcon, GridIcon, MotionIcon, PaletteIcon } from "@/components/ui/icons";
import { downloadText } from "@/lib/download";
import { splitDesignSections } from "@/lib/design-sections";
import { useHistory, useHistoryStatus, useIsHydrated } from "@/lib/history-store";
import { AnimationsPanel, hasAnimationData } from "./animations-panel";
import { ComponentsPanel, hasComponentData } from "./components-panel";
import { DesignPanel, hasDesignData } from "./design-panel";
import { MarkdownPreview } from "./markdown-preview";
import { ResultsSidebar } from "./results-sidebar";

type Tab = "preview" | "raw" | "design" | "components" | "animations";

const TABS: readonly Tab[] = ["preview", "raw", "design", "components", "animations"];
const TAB_LABELS: Record<Tab, string> = {
  preview: "Preview",
  raw: "Raw",
  design: "Design",
  components: "Components",
  animations: "Animations",
};
const TAB_ICONS: Partial<Record<Tab, ReactNode>> = {
  design: <PaletteIcon size={13} />,
  components: <GridIcon size={13} />,
  animations: <MotionIcon size={13} />,
};
type Format = "md" | "txt";

// Text shown inside a tab when the scan has nothing for it. Stores nothing.
function TabNote({ children }: { children: ReactNode }) {
  return <p className="ts-body max-w-xl text-ink-muted">{children}</p>;
}

// Shows one scan's documentation as a two-pane screen. Top bar: "Output | host [.md]" with Copy and
// Download. Left: thumbnail, tech fingerprint and scan details. Right: Preview, Raw, Design,
// Components and Animations tabs, scrolling inside the page on desktop. The Design tab shows the
// "Design system" and "Design breakdown" sections of the document (fonts, colours, components);
// Preview shows the rest; Raw and Copy/Download use the whole document.
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
  const tabRefs = useRef<Record<Tab, HTMLButtonElement | null>>({ preview: null, raw: null, design: null, components: null, animations: null });
  const panelId = useId();

  const scan = hydrated ? (id ? history.find((s) => s.id === id) : history[0]) : undefined;
  const sections = useMemo(() => splitDesignSections(scan?.markdown ?? ""), [scan?.markdown]);

  if (!hydrated) return null;

  if (!scan) {
    return (
      <PageFrame size="narrow">
        <EmptyState action={<ButtonLink href="/history" variant="secondary">Open history</ButtonLink>}>
          {loadError ??
            (id
              ? isApiMode
                ? "That scan was not found."
                : "That scan was not found in this browser."
              : "No results yet. Analyze a website to see its documentation here.")}
        </EmptyState>
      </PageFrame>
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
    downloadText(`${scan!.host}.${format}`, scan!.markdown, format === "md" ? "text/markdown" : "text/plain");
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
    <div className="flex flex-col md:h-screen">
      <header className="flex min-h-[52px] flex-wrap items-center gap-x-3 gap-y-2 border-b border-border-subtle px-4 py-2 md:px-5">
        <span className="ts-label text-ink-muted">Output</span>
        <span aria-hidden="true" className="h-4 w-px bg-border-strong" />
        <h1 className="ts-mono-ui min-w-0 truncate text-ink" title={scan.url}>
          {scan.host}
        </h1>
        <label className="relative">
          <span className="sr-only">File format</span>
          <select
            value={format}
            onChange={(e) => setFormat(e.target.value as Format)}
            className="cursor-pointer appearance-none rounded-sm border border-border-strong bg-bg-surface py-0.5 pl-2 pr-5 font-mono text-[10px] leading-4 text-ink-body"
          >
            <option value="md">.md</option>
            <option value="txt">.txt</option>
          </select>
          <svg aria-hidden="true" viewBox="0 0 8 5" className="pointer-events-none absolute right-1.5 top-1/2 w-2 -translate-y-1/2 text-ink-muted">
            <path d="m1 1 3 3 3-3" fill="none" stroke="currentColor" strokeWidth="1.2" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        </label>
        <div className="ml-auto flex items-center gap-2">
          <Button variant="secondary" onClick={copy} disabled={!canExport} title={canExport ? undefined : exportBlocked}>
            {copyState === "copied" ? <CheckIcon /> : <CopyIcon />}
            {copyState === "copied" ? "Copied" : copyState === "failed" ? "Copy blocked" : "Copy"}
          </Button>
          <Button onClick={download} disabled={!canExport} title={canExport ? undefined : exportBlocked}>
            <DownloadIcon />
            Download
          </Button>
        </div>
      </header>

      <div className="flex flex-1 flex-col md:min-h-0 md:flex-row">
        <aside className="pane-scroll border-b border-border-subtle p-4 md:w-[270px] md:flex-none md:overflow-y-auto md:border-b-0 md:border-r md:p-5">
          <ResultsSidebar scan={scan} />
        </aside>

        <section className="flex min-w-0 flex-1 flex-col md:min-h-0">
          <div className="flex items-center gap-4 border-b border-border-subtle px-2 md:px-7">
            <div role="tablist" aria-label="Output view" onKeyDown={onTabKey} className="flex gap-1 overflow-hidden">
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
                  className={`ts-mono-ui relative inline-flex h-[42px] items-center gap-1.5 whitespace-nowrap px-3 transition-colors ${
                    tab === t ? "text-ink" : "text-ink-muted hover:text-ink"
                  }`}
                >
                  {TAB_ICONS[t]}
                  {TAB_LABELS[t]}
                  {tab === t && <span aria-hidden="true" className="absolute inset-x-3 bottom-0 h-0.5 rounded-full bg-violet-500" />}
                </button>
              ))}
            </div>
            {!canExport && <p className="ts-small ml-auto hidden flex-none text-warning md:block">{exportBlocked}</p>}
          </div>

          <div
            role="tabpanel"
            id={panelId}
            aria-labelledby={`${panelId}-${tab}`}
            tabIndex={0}
            className="pane-scroll min-h-0 flex-1 px-4 py-7 md:overflow-y-auto md:px-12 md:py-9"
          >
            <div className={tab === "preview" || tab === "raw" ? "max-w-[760px]" : "max-w-[1024px]"}>
              {tab === "preview" && <MarkdownPreview source={sections.document} />}
              {tab === "design" &&
                (hasDesignData(scan.analysis?.design) ? (
                  <DesignPanel design={scan.analysis.design} canExport={canExport} />
                ) : sections.design ? (
                  <>
                    <p role="status" className="ts-small mb-4 rounded-md border border-border-strong bg-bg-surface p-3 text-warning">
                      This is a text-only view. This scan has no visual design data (colour swatches, font previews),
                      because it was made before that feature or by an older server. Run a new scan to see them.
                    </p>
                    <MarkdownPreview source={sections.design} />
                  </>
                ) : (
                  <TabNote>
                    No design data for this scan. Scans made in AI mode include the colours, fonts and design tokens here.
                  </TabNote>
                ))}
              {tab === "components" &&
                (hasComponentData(scan.analysis?.components) ? (
                  <ComponentsPanel components={scan.analysis.components} />
                ) : (
                  <TabNote>
                    No component previews for this scan. Scans made in AI mode include small HTML and CSS versions of the
                    site&apos;s buttons, inputs, cards and navigation here; older scans do not have them.
                  </TabNote>
                ))}
              {tab === "animations" &&
                (hasAnimationData(scan.analysis?.animations) ? (
                  <AnimationsPanel report={scan.analysis.animations} />
                ) : (
                  <TabNote>
                    No animation data for this scan. Scans made in AI mode with screenshots turned on list the page&apos;s animations here,
                    with a GSAP or Framer Motion guess and a starter snippet; older scans do not have them.
                  </TabNote>
                ))}
              {tab === "raw" && (
                <pre className="ts-code overflow-x-auto rounded-lg border border-border-subtle bg-bg-sidebar p-5 text-ink-body">{scan.markdown}</pre>
              )}
            </div>
          </div>
        </section>
      </div>
    </div>
  );
}
