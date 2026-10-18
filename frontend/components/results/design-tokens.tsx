"use client";

import type { ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { CopyIcon } from "@/components/ui/icons";
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

function Group({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="space-y-2">
      <h3 className="ts-label text-ink-dim">{title}</h3>
      {children}
    </section>
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
    <div className="space-y-6">
      {framework && (
        <Group title="Utility framework">
          <span className="ts-mono-ui inline-block rounded-md border border-border-strong bg-bg-raised px-3 py-1 text-ink">{framework}</span>
        </Group>
      )}
      {radii.length > 0 && (
        <Group title="Corner radii">
          <ul className="flex flex-wrap gap-4">
            {radii.map((r) => (
              <li key={r} className="space-y-1 text-center">
                <div className="size-16 border border-border-strong bg-bg-raised" style={{ borderRadius: safeLength(r) }} />
                <span className="ts-small text-ink-muted">{r}</span>
              </li>
            ))}
          </ul>
        </Group>
      )}
      {shadows.length > 0 && (
        <Group title="Shadows">
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
        <Group title="Spacing scale">
          <ul className="space-y-1.5">
            {spacing.map((s) => {
              const px = Number.parseFloat(s);
              const width = Number.isFinite(px) ? Math.min(Math.max(px, 2), MAX_BAR_PX) : 2;
              return (
                <li key={s} className="flex items-center gap-3">
                  <span className="ts-small w-14 flex-none text-ink-muted">{s}</span>
                  <span className="h-3 rounded-sm bg-violet-500" style={{ width }} />
                </li>
              );
            })}
          </ul>
        </Group>
      )}
      {vars.length > 0 && (
        <Group title="CSS variables">
          <Button variant="secondary" className="!px-3 !py-1.5" onClick={() => onCopy(rootBlock(vars), ":root block")}>
            <CopyIcon /> Copy all as :root block
          </Button>
          <ul className="grid gap-1 sm:grid-cols-2">
            {vars.map((v) => {
              const isColour = parseHex(v.value.trim()) !== null;
              return (
                <li key={v.name} className="flex items-center gap-2 rounded-md bg-bg-raised px-3 py-1.5">
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
      {breakpoints.length > 0 && (
        <Group title="Breakpoints">
          <ul className="flex flex-wrap gap-2">
            {breakpoints.map((b) => (
              <li key={b} className="ts-mono-ui rounded-pill border border-border-strong bg-bg-raised px-3 py-0.5 text-ink-body">{b}</li>
            ))}
          </ul>
        </Group>
      )}
    </div>
  );
}
