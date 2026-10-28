"use client";

import type { ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { CopyIcon } from "@/components/ui/icons";
import { DesignCard } from "./design-card";
import { parseHex, rootBlock } from "@/lib/design-utils";
import type { DesignSystem } from "@/lib/types";

interface Props {
  design: DesignSystem;
  onCopy: (text: string, label: string) => void;
}

const MAX_BAR_PX = 240;

// Only passes values that look like a plain CSS length, so a bad value cannot break the style.
function safeLength(value: string): string | undefined {
  return /^-?\d*\.?\d+(px|rem|em|%)$/.test(value.trim()) ? value.trim() : undefined;
}

// Allows only characters normal box-shadow values use, so odd text is not applied as CSS.
function safeShadow(value: string): string | undefined {
  return /^[\w\s.,()#%/-]+$/.test(value) ? value : undefined;
}

function Group({ id, title, meta, children, wide }: { id: string; title: string; meta?: string; children: ReactNode; wide?: boolean }) {
  return (
    <DesignCard id={id} title={title} meta={meta} wide={wide}>
      <div className="space-y-3">{children}</div>
    </DesignCard>
  );
}

// Token section: radii and shadow boxes, spacing bars, CSS variables with a copy-as-:root button,
// breakpoints and the utility framework. Everything is optional. Stores nothing.
export function DesignTokens({ design, onCopy }: Props) {
  const radii = design.radii ?? [];
  const shadows = design.shadows ?? [];
  const spacing = design.spacingScale ?? [];
  const vars = (design.cssVariables ?? []).filter((v) => v && typeof v.name === "string" && typeof v.value === "string");
  const breakpoints = design.breakpoints ?? [];
  const framework = design.utilityFramework;
  return (
    <>
      {radii.length > 0 && (
        <Group id="design-radii" title="Corner radii" meta={`${radii.length} values`}>
          <ul className="grid grid-cols-2 gap-3">
            {radii.map((r) => (
              <li key={r} className="flex items-center gap-2.5">
                <div className="size-9 flex-none border border-border-strong bg-bg-raised" style={{ borderRadius: safeLength(r) }} />
                <span className="font-mono text-[11px] text-ink-muted">{r}</span>
              </li>
            ))}
          </ul>
        </Group>
      )}
      {breakpoints.length > 0 && (
        <Group id="design-breakpoints" title="Breakpoints" meta={`${breakpoints.length} ranges`}>
          <ul className="divide-y divide-border-subtle">
            {breakpoints.map((b) => (
              <li key={b} className="py-2 font-mono text-[12px] text-ink-body first:pt-0 last:pb-0">{b}</li>
            ))}
          </ul>
        </Group>
      )}
      {shadows.length > 0 && (
        <Group id="design-shadows" title="Shadows" meta={`${shadows.length} values`}>
          <ul className="flex flex-wrap gap-6">
            {shadows.map((s) => (
              <li key={s} className="max-w-40 space-y-2 text-center">
                <div className="mx-auto size-16 rounded-md bg-ink-body" style={{ boxShadow: safeShadow(s) }} />
                <span className="ts-small block break-words text-ink-muted">{s}</span>
              </li>
            ))}
          </ul>
        </Group>
      )}
      {spacing.length > 0 && (
        <Group id="design-spacing" title="Spacing scale" meta={`${spacing.length} steps`}>
          <ul className="space-y-1.5">
            {spacing.map((s) => {
              const px = Number.parseFloat(s);
              const width = Number.isFinite(px) ? Math.min(Math.max(px, 2), MAX_BAR_PX) : 2;
              return (
                <li key={s} className="flex items-center gap-3">
                  <span className="font-mono text-[11px] w-14 flex-none text-ink-muted">{s}</span>
                  <span className="h-3 rounded-sm bg-plum-400" style={{ width }} />
                </li>
              );
            })}
          </ul>
        </Group>
      )}
      {framework && (
        <Group id="design-framework" title="Utility framework">
          <span className="ts-mono-ui inline-block rounded-md border border-border-strong bg-bg-raised px-3 py-1 text-ink">{framework}</span>
        </Group>
      )}
      {vars.length > 0 && (
        <Group id="design-vars" title="CSS variables" meta={`${vars.length} variables`} wide>
          <Button variant="secondary" className="!px-3 !py-1.5" onClick={() => onCopy(rootBlock(vars), ":root block")}>
            <CopyIcon /> Copy all as :root block
          </Button>
          <ul className="grid gap-1 sm:grid-cols-2">
            {vars.map((v) => {
              const isColour = parseHex(v.value.trim()) !== null;
              return (
                <li key={v.name} className="flex items-center gap-2 rounded-md bg-bg-base px-3 py-1.5">
                  {isColour && (
                    <span className="size-4 flex-none rounded-sm border border-border-strong" style={{ backgroundColor: v.value.trim() }} aria-hidden="true" />
                  )}
                  <code className="ts-code min-w-0 flex-1 truncate text-ink-body" title={`${v.name}: ${v.value}`}>{v.name}</code>
                  <code className="ts-code max-w-[50%] truncate text-ink-muted" title={v.value}>{v.value}</code>
                </li>
              );
            })}
          </ul>
        </Group>
      )}
    </>
  );
}
