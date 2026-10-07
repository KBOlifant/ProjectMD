"use client";

import { useId, useRef, useState } from "react";
import { Button, ButtonLink } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/empty-state";
import { CheckIcon, CopyIcon, DownloadIcon } from "@/components/ui/icons";
import { ConfidenceBadge, TechChip } from "@/components/ui/tech-chip";
import { formatDate, formatSeconds, initials } from "@/lib/format";
import { useHistory, useIsHydrated } from "@/lib/history-store";
import { MarkdownPreview } from "./markdown-preview";

type Tab = "preview" | "raw";
type Format = "md" | "txt";

// Shows one scan's documentation: header, copy/download buttons, tech chips and Preview/Raw tabs.
// Finds the scan in saved history by `id` (or the newest scan if no id). Only reads history;
// the tab, format and copy status are temporary UI state and are not stored.
export function ResultsView({ id }: { id?: string }) {
  const history = useHistory();
  const hydrated = useIsHydrated();
  const [tab, setTab] = useState<Tab>("preview");
  const [format, setFormat] = useState<Format>("md");
  const [copyState, setCopyState] = useState<"idle" | "copied" | "failed">("idle");
  const tabRefs = useRef<Record<Tab, HTMLButtonElement | null>>({ preview: null, raw: null });
  const panelId = useId();

  if (!hydrated) return null;

  const scan = id ? history.find((s) => s.id === id) : history[0];
  if (!scan) {
    return (
      <EmptyState action={<ButtonLink href="/history" variant="secondary">Open History</ButtonLink>}>
        {id ? "That scan was not found in this browser." : "No results yet. Generate a scan to see its documentation here."}
      </EmptyState>
    );
  }

  // Copies the scan's markdown to the clipboard and flashes "Copied"/"Copy blocked" for 1.5s. Stores nothing.
  async function copy() {
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
    const blob = new Blob([scan!.markdown], { type: format === "md" ? "text/markdown" : "text/plain" });
    const href = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = href;
    a.download = `${scan!.host}.${format}`;
    a.click();
    URL.revokeObjectURL(href);
  }

  // Lets the left/right arrow keys switch between the Preview and Raw tabs. Stores nothing.
  function onTabKey(e: React.KeyboardEvent) {
    if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return;
    const next: Tab = tab === "preview" ? "raw" : "preview";
    setTab(next);
    tabRefs.current[next]?.focus();
  }

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-center gap-4">
        <span className="grid size-[38px] flex-none place-items-center rounded-md bg-violet-700 font-mono text-[11px] font-medium text-ink">
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
        <Button variant="secondary" onClick={copy} className="ml-auto">
          {copyState === "copied" ? <CheckIcon /> : <CopyIcon />}
          {copyState === "copied" ? "Copied" : copyState === "failed" ? "Copy blocked" : "Copy"}
        </Button>
        <Button onClick={download}>
          <DownloadIcon />
          Download
        </Button>
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
          {(["preview", "raw"] as const).map((t) => (
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
                tab === t ? "border-violet-500 text-ink" : "border-transparent text-ink-muted hover:text-ink"
              }`}
            >
              {t === "preview" ? "Preview" : "Raw"}
            </button>
          ))}
        </div>
        <div
          role="tabpanel"
          id={panelId}
          aria-labelledby={`${panelId}-${tab}`}
          className="rounded-b-lg border border-t-0 border-border-subtle bg-bg-surface p-6"
        >
          {tab === "preview" ? (
            <MarkdownPreview source={scan.markdown} />
          ) : (
            <pre className="ts-code overflow-x-auto text-ink-body">{scan.markdown}</pre>
          )}
        </div>
      </div>
    </div>
  );
}
