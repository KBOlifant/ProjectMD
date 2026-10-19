"use client";

import { useState } from "react";
import type { ComponentPreview, ComponentPreviews } from "@/lib/types";
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

function Preview({ item, dark, onCopy }: { item: ComponentPreview; dark: boolean; onCopy: (text: string, label: string) => void }) {
  return (
    <li className="overflow-hidden rounded-lg border border-border-strong bg-bg-raised">
      <iframe
        title={`${item.name} preview`}
        sandbox=""
        referrerPolicy="no-referrer"
        srcDoc={frameDocument(item, dark)}
        className="block h-48 w-full border-0"
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

// The Components tab: small AI-written HTML/CSS versions of the site's main components, each in a
// sandboxed frame with copy buttons. Only reads the scan; the background choice is temporary UI state.
export function ComponentsPanel({ components }: { components: ComponentPreviews }) {
  const { message, copy } = useCopy();
  const [dark, setDark] = useState(false);
  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center gap-4">
        <p role="status" aria-live="polite" className="ts-mono-ui min-h-5 flex-1 text-success">{message}</p>
        <button
          type="button"
          aria-pressed={dark}
          onClick={() => setDark((d) => !d)}
          className="ts-mono-ui rounded-md border border-border-strong px-3 py-1.5 text-ink-muted hover:text-ink"
        >
          {dark ? "Dark background" : "Light background"}
        </button>
      </div>
      <p className="ts-small text-ink-muted">
        These are approximate, AI-written versions built from the styles measured on the page. Check them against the real site before using them.
      </p>
      <ul className="grid gap-4 md:grid-cols-2">
        {components.items.map((item, i) => (
          <Preview key={`${item.kind}-${i}`} item={item} dark={dark} onCopy={copy} />
        ))}
      </ul>
    </div>
  );
}
