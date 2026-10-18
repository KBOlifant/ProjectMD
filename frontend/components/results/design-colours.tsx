"use client";

import { cleanColours, pickMainColours, readableTextOn } from "@/lib/design-utils";
import type { DesignColor } from "@/lib/types";

interface Props {
  colors: readonly DesignColor[] | undefined;
  onCopy: (text: string, label: string) => void;
}

function describe(c: DesignColor): string {
  return [c.role, c.uses !== undefined ? `${c.uses} uses` : null].filter(Boolean).join(" · ");
}

// Colour section: the main colours as big clickable swatches, every other colour as a small clickable
// square. Clicking copies the hex. Reads the colours only; stores nothing.
export function DesignColours({ colors, onCopy }: Props) {
  const clean = cleanColours(colors);
  if (clean.length === 0) return null;
  const { main, others } = pickMainColours(clean);
  return (
    <section aria-labelledby="design-colours" className="space-y-3">
      <h3 id="design-colours" className="ts-label text-ink-dim">Colours</h3>
      <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        {main.map((c) => (
          <li key={c.hex}>
            <button
              type="button"
              onClick={() => onCopy(c.hex, c.hex)}
              aria-label={`Copy ${c.hex}${describe(c) ? `, ${describe(c)}` : ""}`}
              style={{ backgroundColor: c.hex, color: readableTextOn(c.hex) }}
              className="flex aspect-square w-full flex-col justify-end rounded-lg border border-border-strong p-3 text-left transition-transform hover:scale-[1.02] motion-reduce:transition-none motion-reduce:hover:scale-100"
            >
              <span className="ts-mono-ui">{c.hex}</span>
              <span className="ts-small opacity-80">{describe(c)}</span>
            </button>
          </li>
        ))}
      </ul>
      {others.length > 0 && (
        <ul className="flex flex-wrap gap-2" aria-label="Other colours">
          {others.map((c) => (
            <li key={c.hex}>
              <button
                type="button"
                onClick={() => onCopy(c.hex, c.hex)}
                aria-label={`Copy ${c.hex}${describe(c) ? `, ${describe(c)}` : ""}`}
                title={`${c.hex}${describe(c) ? ` · ${describe(c)}` : ""}`}
                style={{ backgroundColor: c.hex }}
                className="size-8 rounded-md border border-border-strong transition-transform hover:scale-110 motion-reduce:transition-none motion-reduce:hover:scale-100"
              />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
