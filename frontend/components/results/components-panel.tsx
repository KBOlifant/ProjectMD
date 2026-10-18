"use client";

import { useState } from "react";
import { googleFontsUrl } from "@/lib/design-utils";
import type { ComponentPreview, ComponentPreviews, DesignFont } from "@/lib/types";
import { useCopy } from "./use-copy";

type Backdrop = "page" | "light" | "dark";

const BACKDROP_LABELS: Record<Backdrop, string> = { page: "Page background", light: "Light background", dark: "Dark background" };
const NEXT_BACKDROP: Record<Backdrop, Backdrop> = { page: "light", light: "dark", dark: "page" };
const MIN_FRAME = 120;
const MAX_FRAME = 420;
const FRAME_PADDING = 56;

// True when a scan has at least one component preview worth showing.
export function hasComponentData(components: ComponentPreviews | null | undefined): components is ComponentPreviews {
  return Boolean(components?.items && components.items.length > 0);
}

// The copyable code for one component: its CSS in a style block, then its HTML.
export function componentCode(item: ComponentPreview): string {
  return item.css ? `<style>\n${item.css}\n</style>\n\n${item.html}` : item.html;
}

function backgroundFor(item: ComponentPreview, backdrop: Backdrop): string {
  if (backdrop === "light") return "#ffffff";
  if (backdrop === "dark") return "#0b0b12";
  return /^#[0-9a-fA-F]{6}$/.test(item.background ?? "") ? (item.background as string) : "#ffffff";
}

// How tall the frame is: the component's real height plus room around it, within limits.
function frameHeight(item: ComponentPreview): number {
  return Math.min(MAX_FRAME, Math.max(MIN_FRAME, (item.height ?? 0) + FRAME_PADDING));
}

// The page shown inside the preview frame. The server already cleaned the HTML and CSS; the frame is a
// second layer: scripts, forms and same-origin access are off, and the policy allows only the inline
// style plus Google Fonts (so the site's fonts can show). "</" is broken up in the CSS so it can never
// close the style tag.
function frameDocument(item: ComponentPreview, backdrop: Backdrop, fontUrls: readonly string[]): string {
  const css = item.css.replaceAll("</", "<\\/");
  const links = fontUrls.map((url) => `<link rel="stylesheet" href="${url}">`).join("");
  return (
    `<!doctype html><meta charset="utf-8">` +
    `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline' https://fonts.googleapis.com; font-src https://fonts.gstatic.com">` +
    links +
    `<style>html,body{margin:0}body{box-sizing:border-box;min-height:100vh;padding:24px;display:flex;align-items:center;justify-content:center;background:${backgroundFor(item, backdrop)};font-family:system-ui,sans-serif}</style>` +
    `<style>${css}</style>${item.html}`
  );
}

interface PreviewProps {
  item: ComponentPreview;
  backdrop: Backdrop;
  fontUrls: readonly string[];
  onCopy: (text: string, label: string) => void;
}

function Preview({ item, backdrop, fontUrls, onCopy }: PreviewProps) {
  return (
    <li className="overflow-hidden rounded-lg border border-border-strong bg-bg-raised">
      <iframe
        title={`${item.name} preview`}
        sandbox=""
        referrerPolicy="no-referrer"
        srcDoc={frameDocument(item, backdrop, fontUrls)}
        style={{ height: frameHeight(item) }}
        className="block w-full border-0"
      />
      <div className="flex flex-wrap items-center gap-2 border-t border-border-subtle p-3">
        <div className="min-w-0 flex-1 basis-32">
          <p className="ts-label truncate text-ink">{item.name}</p>
          <p className="ts-small text-ink-dim">{item.kind}</p>
        </div>
        <button type="button" onClick={() => onCopy(item.html, `${item.name} HTML`)} className="ts-mono-ui rounded-md border border-border-strong px-3 py-1.5 text-ink-muted hover:text-ink">
          Copy HTML
        </button>
        <button type="button" onClick={() => onCopy(item.css, `${item.name} CSS`)} className="ts-mono-ui rounded-md border border-border-strong px-3 py-1.5 text-ink-muted hover:text-ink">
          Copy CSS
        </button>
        <button type="button" onClick={() => onCopy(componentCode(item), `${item.name} code`)} className="ts-mono-ui rounded-md bg-violet-700 px-3 py-1.5 text-ink hover:bg-violet-600">
          Copy code
        </button>
      </div>
    </li>
  );
}

interface PanelProps {
  components: ComponentPreviews;
  /** The scan's fonts; Google Fonts among them are loaded inside the frames so text looks right. */
  fonts?: readonly DesignFont[];
}

// The Components tab: HTML and CSS rebuilt from the real elements of the site (their tags, nesting and
// measured styles), each in a sandboxed frame on the colour of the original page, with copy buttons.
// Only reads the scan; the background choice is temporary UI state.
export function ComponentsPanel({ components, fonts }: PanelProps) {
  const { message, copy } = useCopy();
  const [backdrop, setBackdrop] = useState<Backdrop>("page");
  const fontUrls = (fonts ?? []).map(googleFontsUrl).filter((url): url is string => Boolean(url)).slice(0, 4);
  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center gap-4">
        <p role="status" aria-live="polite" className="ts-mono-ui min-h-5 flex-1 text-success">{message}</p>
        <button
          type="button"
          onClick={() => setBackdrop((b) => NEXT_BACKDROP[b])}
          className="ts-mono-ui rounded-md border border-border-strong px-3 py-1.5 text-ink-muted hover:text-ink"
        >
          {BACKDROP_LABELS[backdrop]}
        </button>
      </div>
      <p className="ts-small text-ink-muted">
        Rebuilt from the styles measured on the page. Text is sample text, icons and images are left out, and hover states are not captured, so check
        them against the real site before using them.
      </p>
      <ul className="grid gap-4 md:grid-cols-2">
        {components.items.map((item, i) => (
          <Preview key={`${item.kind}-${i}`} item={item} backdrop={backdrop} fontUrls={fontUrls} onCopy={copy} />
        ))}
      </ul>
    </div>
  );
}
