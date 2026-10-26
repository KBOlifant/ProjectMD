"use client";

import { useState } from "react";
import { CopyIcon, GridIcon } from "@/components/ui/icons";
import type { ComponentPreview, ComponentPreviews } from "@/lib/types";
import { CountBadge, TabHeader } from "./tab-header";
import { useCopy } from "./use-copy";

// True when a scan has at least one component preview worth showing.
export function hasComponentData(components: ComponentPreviews | null | undefined): components is ComponentPreviews {
  return Boolean(components?.items && components.items.length > 0);
}

// The copyable code for one component: its CSS in a style block, then its HTML.
export function componentCode(item: ComponentPreview): string {
  return item.css ? `<style>\n${item.css}\n</style>\n\n${item.html}` : item.html;
}

// The page shown inside the preview frame. The server already cleaned the HTML and CSS; the frame
// is a second layer: scripts, forms and same-origin access are off, and the policy allows nothing
// except the inline style. "</" is broken up in the CSS so it can never close the style tag.
function frameDocument(item: ComponentPreview, dark: boolean): string {
  const background = dark ? "#0b0b12" : "#ffffff";
  const css = item.css.replaceAll("</", "<\\/");
  return (
    `<!doctype html><meta charset="utf-8">` +
    `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">` +
    `<style>html,body{margin:0}body{box-sizing:border-box;min-height:100vh;padding:24px;display:flex;align-items:center;justify-content:center;background:${background};font-family:system-ui,sans-serif}</style>` +
    `<style>${css}</style>${item.html}`
  );
}

const SMALL_BUTTON =
  "ts-mono-ui inline-flex items-center gap-1.5 rounded-md border border-border-strong px-3 py-1.5 text-ink-body transition-colors hover:bg-bg-raised hover:text-ink";

function Preview({ item, index, dark, onCopy }: { item: ComponentPreview; index: number; dark: boolean; onCopy: (text: string, label: string) => void }) {
  return (
    <li className="overflow-hidden rounded-xl border border-border-subtle bg-bg-surface">
      <div className="flex flex-wrap items-center gap-3 border-b border-border-subtle px-5 py-3.5">
        <span aria-hidden="true" className="font-mono text-[10px] text-plum-300">{String(index + 1).padStart(2, "0")}</span>
        <div className="min-w-0 flex-1 basis-40">
          <h3 className="ts-mono-ui truncate font-semibold text-ink">{item.name}</h3>
          <p className="font-mono text-[10px] leading-4 text-ink-muted">{item.kind}</p>
        </div>
        <button type="button" onClick={() => onCopy(item.html, `${item.name} HTML`)} className={SMALL_BUTTON}>
          Copy HTML
        </button>
        <button type="button" onClick={() => onCopy(item.css, `${item.name} CSS`)} className={SMALL_BUTTON}>
          Copy CSS
        </button>
        <button type="button" onClick={() => onCopy(componentCode(item), `${item.name} code`)} className={SMALL_BUTTON}>
          <CopyIcon size={13} />
          Copy code
        </button>
      </div>
      <iframe
        title={`${item.name} preview`}
        sandbox=""
        referrerPolicy="no-referrer"
        srcDoc={frameDocument(item, dark)}
        className="block h-48 w-full border-0"
      />
      <pre className="ts-code max-h-64 overflow-auto border-t border-border-subtle bg-bg-sidebar p-5 text-ink-body">{componentCode(item)}</pre>
    </li>
  );
}

// The Components tab: small AI-written HTML/CSS versions of the site's main components, each with a
// sandboxed live preview, its code and copy buttons. Only reads the scan; the background choice is
// temporary UI state.
export function ComponentsPanel({ components }: { components: ComponentPreviews }) {
  const { message, copy } = useCopy();
  const [dark, setDark] = useState(false);
  const count = components.items.length;
  return (
    <div className="space-y-6">
      <TabHeader
        eyebrow="Reusable UI"
        title="Captured components"
        description="Patterns reconstructed from the scanned site, ready to copy."
        aside={<CountBadge icon={<GridIcon size={13} />}>{count} {count === 1 ? "component" : "components"} found</CountBadge>}
      />
      <div className="flex flex-wrap items-center gap-4">
        <p role="status" aria-live="polite" className="ts-mono-ui min-h-5 flex-1 text-success">{message}</p>
        <button
          type="button"
          aria-pressed={dark}
          onClick={() => setDark((d) => !d)}
          className={SMALL_BUTTON}
        >
          {dark ? "Dark background" : "Light background"}
        </button>
      </div>
      <p className="ts-small text-ink-muted">
        These are approximate, AI-written versions built from the styles measured on the page. Check them against the real site before using them.
      </p>
      <ol className="space-y-4">
        {components.items.map((item, i) => (
          <Preview key={`${item.kind}-${i}`} item={item} index={i} dark={dark} onCopy={copy} />
        ))}
      </ol>
    </div>
  );
}
