import type { ReactNode } from "react";
import { AtomIcon, CubeIcon, DiscIcon, SearchIcon, TriangleIcon, UserSquareIcon, WaveIcon } from "./icons";

// Picks a small generic glyph for a detected technology by name. These are shapes, not brand logos;
// anything unknown gets the cube. Pure lookup; stores nothing.
function techGlyph(name: string): ReactNode {
  const n = name.toLowerCase();
  if (/react|next|preact|remix/.test(n)) return <AtomIcon size={12} />;
  if (/vercel|netlify|cloudflare|aws|edge/.test(n)) return <TriangleIcon size={12} />;
  if (/tailwind|css|sass|styled|radix/.test(n)) return <WaveIcon size={12} />;
  if (/segment|analytics|posthog|plausible|gtag|google/.test(n)) return <DiscIcon size={12} />;
  if (/clerk|auth|firebase|okta/.test(n)) return <UserSquareIcon size={12} />;
  if (/algolia|search|elastic/.test(n)) return <SearchIcon size={12} />;
  return <CubeIcon size={12} />;
}

// Tile showing one detected technology (e.g. "React 19") with a glyph, used in the Results side
// panel; `highlighted` gives it the violet border. Stores nothing.
export function TechChip({ name, highlighted = false }: { name: string; highlighted?: boolean }) {
  return (
    <span
      className={`flex min-h-9 min-w-0 items-center gap-2 rounded-md border bg-bg-surface px-2 py-1 ${
        highlighted ? "border-violet-500" : "border-border-subtle"
      }`}
    >
      <span className="grid size-5 flex-none place-items-center rounded-sm bg-bg-raised text-plum-300">{techGlyph(name)}</span>
      <span className="min-w-0 break-words font-mono text-[10px] leading-[14px] text-ink-body">
        {name}
      </span>
    </span>
  );
}

// Plain outlined tag for dense lists (History rows). No glyph. Stores nothing.
export function TechTag({ name }: { name: string }) {
  return (
    <span className="inline-block rounded-sm border border-border-strong px-1.5 py-px font-mono text-[10px] leading-4 text-ink-muted">
      {name}
    </span>
  );
}

// Badge showing the scan's confidence %, green at 95+, amber under 80. The visible text is just the
// number; the accessible name says what it is. Stores nothing.
export function ConfidenceBadge({ value }: { value: number }) {
  const tone = value >= 95 ? "border-success/40 text-success" : value < 80 ? "border-warning/50 text-warning" : "border-border-strong text-ink-muted";
  return (
    <span
      title={`${value}% confidence`}
      className={`inline-block rounded-sm border px-1.5 py-px font-mono text-[10px] leading-4 ${tone}`}
    >
      <span aria-hidden="true">{value}%</span>
      <span className="sr-only">{value}% confidence</span>
    </span>
  );
}
