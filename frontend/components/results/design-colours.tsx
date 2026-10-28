"use client";

import { cleanColours, pickMainColours, readableTextOn } from "@/lib/design-utils";
import type { DesignColor } from "@/lib/types";
import { DesignCard } from "./design-card";

interface Props {
  colors: readonly DesignColor[] | undefined;
  onCopy: (text: string, label: string) => void;
}

function describe(c: DesignColor): string {
  return [c.role, c.uses !== undefined ? `${c.uses} uses` : null].filter(Boolean).join(" · ");
}

// Colour section: the main colours as big clickable swatches with a label bar, every other colour as
// a small chip. Clicking copies the hex. Reads the colours only; stores nothing.
export function DesignColours({ colors, onCopy }: Props) {
  const clean = cleanColours(colors);
  if (clean.length === 0) return null;
  const { main, others } = pickMainColours(clean);
  return (
    <DesignCard id="design-colours" title="Colours" meta={`${clean.length} ${clean.length === 1 ? "value" : "values"}`} wide className="space-y-3">
      <ul className="grid grid-cols-2 gap-3 md:grid-cols-3">
        {main.map((c) => (
          <li key={c.hex}>
            <button
              type="button"
              onClick={() => onCopy(c.hex, c.hex)}
              aria-label={`Copy ${c.hex}${describe(c) ? `, ${describe(c)}` : ""}`}
              className="group block w-full overflow-hidden rounded-lg border border-border-strong text-left"
            >
              <span style={{ backgroundColor: c.hex, color: readableTextOn(c.hex) }} className="block h-20 px-3 py-2 font-mono text-[11px]">
                <span className="opacity-0 transition-opacity group-hover:opacity-100 group-focus-visible:opacity-100">Click to copy</span>
              </span>
              <span className="flex items-center justify-between gap-2 bg-bg-raised px-3 py-2">
                <span className="ts-mono-ui truncate text-[12px] font-semibold text-ink">{c.role ?? c.hex}</span>
                <span className="font-mono text-[11px] uppercase text-ink-muted">{c.hex}</span>
              </span>
            </button>
          </li>
        ))}
      </ul>
      {others.length > 0 && (
        <ul className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5" aria-label="Other colours">
          {others.map((c) => (
            <li key={c.hex}>
              <button
                type="button"
                onClick={() => onCopy(c.hex, c.hex)}
                aria-label={`Copy ${c.hex}${describe(c) ? `, ${describe(c)}` : ""}`}
                title={`${c.hex}${describe(c) ? ` · ${describe(c)}` : ""}`}
                className="flex w-full items-center gap-2.5 rounded-lg border border-border-subtle px-2.5 py-2 text-left transition-colors hover:bg-bg-hover"
              >
                <span style={{ backgroundColor: c.hex }} className="size-5 flex-none rounded-sm border border-border-strong" />
                <span className="min-w-0">
                  <span className="block truncate font-mono text-[11px] leading-4 text-ink">{c.role ?? "Colour"}</span>
                  <span className="block font-mono text-[11px] uppercase leading-4 text-ink-muted">{c.hex}</span>
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </DesignCard>
  );
}
