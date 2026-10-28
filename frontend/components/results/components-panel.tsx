"use client";

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

const LIGHT = "#ffffff";
const DARK = "#0b0b12";
const HEX = /^#[0-9a-f]{6}$/i;

// Relative brightness (0-1) of a #rgb/#rrggbb or rgb() colour, or null when it can't be read.
function brightness(value: string): number | null {
  const hex = value.match(/^#([0-9a-f]{3}|[0-9a-f]{6})$/i)?.[1];
  const rgb = hex
    ? (hex.length === 3 ? [...hex].map((c) => c + c) : hex.match(/../g)!).map((h) => parseInt(h, 16))
    : value.match(/^rgba?\(\s*(\d+)[\s,]+(\d+)[\s,]+(\d+)/i)?.slice(1).map(Number);
  if (!rgb) return null;
  const [r, g, b] = rgb.map((c) => c / 255);
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

// The colour behind the real component when the scan measured it. Older scans don't have it, so
// pick white or near-black against the first text colour in the CSS (light text gets the dark one).
function frameBackground(item: ComponentPreview): string {
  if (item.background && HEX.test(item.background)) return item.background;
  const text = item.css.match(/(?:^|[\s;{])color:\s*([^;}]+)/)?.[1]?.trim();
  const light = text ? brightness(text) : null;
  return light !== null && light > 0.6 ? DARK : LIGHT;
}

// The page shown inside the preview frame. The server already cleaned the HTML and CSS; the frame
// is a second layer: scripts, forms and same-origin access are off, and the policy allows nothing
// except the inline style. "</" is broken up in the CSS so it can never close the style tag.
// "safe center" keeps a component taller or wider than the frame from being cut off at the top.
function frameDocument(item: ComponentPreview): string {
  const css = item.css.replaceAll("</", "<\\/");
  return (
    `<!doctype html><meta charset="utf-8">` +
    `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">` +
    `<style>html,body{margin:0}body{box-sizing:border-box;min-height:100vh;padding:24px;display:flex;align-items:safe center;justify-content:safe center;overflow:auto;background:${frameBackground(item)};font-family:system-ui,sans-serif}</style>` +
    `<style>${css}</style>${item.html}`
  );
}

const SMALL_BUTTON =
  "ts-mono-ui inline-flex items-center gap-1.5 rounded-md border border-border-strong px-3 py-1.5 text-ink-body transition-colors hover:bg-bg-hover hover:text-ink-hover";

function Preview({ item, index, onCopy }: { item: ComponentPreview; index: number; onCopy: (text: string, label: string) => void }) {
  return (
    <li className="overflow-hidden rounded-xl border border-border-subtle bg-bg-surface">
      <div className="flex flex-wrap items-center gap-3 border-b border-border-subtle px-5 py-3.5">
        <span aria-hidden="true" className="font-mono text-[11px] text-plum-300">{String(index + 1).padStart(2, "0")}</span>
        <div className="min-w-0 flex-1 basis-40">
          <h3 className="ts-mono-ui truncate font-semibold text-ink">{item.name}</h3>
          <p className="font-mono text-[11px] leading-4 text-ink-muted">{item.kind}</p>
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
        srcDoc={frameDocument(item)}
        className="block h-48 w-full border-0"
      />
      <pre className="ts-code max-h-64 overflow-auto border-t border-border-subtle bg-bg-sidebar p-5 text-ink-body">{componentCode(item)}</pre>
    </li>
  );
}

// The Components tab: small AI-written HTML/CSS versions of the site's main components, each with a
// sandboxed live preview on the colour it sat on in the real site, its code and copy buttons. Only
// reads the scan.
export function ComponentsPanel({ components }: { components: ComponentPreviews }) {
  const { message, copy } = useCopy();
  const count = components.items.length;
  return (
    <div className="space-y-6">
      <TabHeader
        eyebrow="Reusable UI"
        title="Captured components"
        description="Patterns reconstructed from the scanned site, ready to copy."
        aside={<CountBadge icon={<GridIcon size={13} />}>{count} {count === 1 ? "component" : "components"} found</CountBadge>}
      />
      <p role="status" aria-live="polite" className="ts-mono-ui min-h-5 text-success">{message}</p>
      <p className="ts-small text-ink-muted">
        These are approximate, AI-written versions built from the styles measured on the page. Check them against the real site before using them.
      </p>
      <ol className="space-y-4">
        {components.items.map((item, i) => (
          <Preview key={`${item.kind}-${i}`} item={item} index={i} onCopy={copy} />
        ))}
      </ol>
    </div>
  );
}
