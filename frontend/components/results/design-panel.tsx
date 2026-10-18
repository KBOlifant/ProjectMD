"use client";

import type { DesignSystem } from "@/lib/types";
import { DesignColours } from "./design-colours";
import { DesignFonts } from "./design-fonts";
import { DesignTokens } from "./design-tokens";
import { useCopy } from "./use-copy";

// True when a design record has anything worth showing. Missing or partial data counts as empty.
export function hasDesignData(design: DesignSystem | null | undefined): design is DesignSystem {
  if (!design) return false;
  return Object.values(design).some((v) => (Array.isArray(v) ? v.length > 0 : Boolean(v)));
}

// The visual Design tab: colour swatches, Google Font previews and design tokens for one scan.
// Copy messages go to a polite live region. Reads the design data only; stores nothing.
export function DesignPanel({ design }: { design: DesignSystem }) {
  const { message, copy } = useCopy();
  return (
    <div className="space-y-8">
      <p role="status" aria-live="polite" className="ts-mono-ui min-h-5 text-success">{message}</p>
      <DesignColours colors={design.colors} onCopy={copy} />
      <DesignFonts fonts={design.fonts} onCopy={copy} />
      <DesignTokens design={design} onCopy={copy} />
    </div>
  );
}
