import type { ReactNode } from "react";

interface TabHeaderProps {
  eyebrow: string;
  title: string;
  description: string;
  // Small outlined badge at the right ("24 tokens captured"); anything else (a toggle) also fits.
  aside?: ReactNode;
}

// Heading block at the top of the Design, Components and Animations tabs: plum eyebrow, title,
// one-line description, and an optional badge or control aligned to the right. Stores nothing.
export function TabHeader({ eyebrow, title, description, aside }: TabHeaderProps) {
  return (
    <header className="flex flex-wrap items-start justify-between gap-4">
      <div className="min-w-0">
        <p className="ts-label text-plum-300">{eyebrow}</p>
        <h2 className="ts-h1 mt-2 text-ink">{title}</h2>
        <p className="ts-body mt-1.5 text-ink-muted">{description}</p>
      </div>
      {aside}
    </header>
  );
}

// Outlined count badge with an icon, used as the `aside` of a TabHeader. Stores nothing.
export function CountBadge({ icon, children }: { icon: ReactNode; children: ReactNode }) {
  return (
    <span className="inline-flex flex-none items-center gap-2 rounded-lg border border-border-strong bg-bg-surface px-3 py-2 font-mono text-[11px] text-ink-body">
      <span className="text-plum-300">{icon}</span>
      {children}
    </span>
  );
}
