"use client";

import { PaletteIcon } from "@/components/ui/icons";
import { hasExportableDesign } from "@/lib/design-export";
import type { DesignSystem } from "@/lib/types";
import { DesignColours } from "./design-colours";
import { DesignExport } from "./design-export";
import { DesignFonts } from "./design-fonts";
import { DesignTokens } from "./design-tokens";
import { CountBadge, TabHeader } from "./tab-header";
import { useCopy } from "./use-copy";

// True when a design record has anything worth showing. Missing or partial data counts as empty.
export function hasDesignData(design: DesignSystem | null | undefined): design is DesignSystem {
  if (!design) return false;
  return Object.values(design).some((v) => (Array.isArray(v) ? v.length > 0 : Boolean(v)));
}

// How many separate design values a scan captured (colours, fonts, radii, shadows, spacing steps,
// CSS variables, breakpoints), for the "N tokens captured" badge. Pure count; stores nothing.
function countTokens(design: DesignSystem): number {
  return [design.colors, design.fonts, design.radii, design.shadows, design.spacingScale, design.cssVariables, design.breakpoints].reduce(
    (sum, list) => sum + (list?.length ?? 0),
    0,
  );
}

// The visual Design tab: header with the token count, colour swatches, Google Font previews, design
// tokens and the export cards for one scan. Copy messages go to a polite live region.
// Reads the design data only; stores nothing.
export function DesignPanel({ design, canExport = true }: { design: DesignSystem; canExport?: boolean }) {
  const { message, copy } = useCopy();
  const count = countTokens(design);
  return (
    <div className="space-y-6">
      <TabHeader
        eyebrow="Captured design system"
        title="Design tokens"
        description="Portable values extracted from the site, ready for design and development tools."
        aside={<CountBadge icon={<PaletteIcon size={13} />}>{count} {count === 1 ? "token" : "tokens"} captured</CountBadge>}
      />
      <p role="status" aria-live="polite" className="ts-mono-ui min-h-5 text-success">{message}</p>
      <DesignColours colors={design.colors} onCopy={copy} />
      <DesignFonts fonts={design.fonts} onCopy={copy} />
      <DesignTokens design={design} onCopy={copy} />
      {hasExportableDesign(design) && <DesignExport design={design} onCopy={copy} canExport={canExport} />}
    </div>
  );
}
