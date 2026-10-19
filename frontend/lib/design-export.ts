import { cleanColours, fontFamilyCss } from "./design-utils";
import type { DesignColor, DesignSystem } from "./types";

// Turns a scan's design system into files other tools can read: Figma design tokens (Tokens Studio
// JSON), CSS variables, and a Tailwind config. Pure functions: no React, no storage. The values come
// from a page we do not control, so every one is checked before it is written, and anything odd is
// left out instead of being passed through.

export interface ExportFormat {
  id: "figma" | "css" | "tailwind";
  label: string;
  description: string;
  filename: string;
  mime: string;
  build: (design: DesignSystem) => string;
}

const LENGTH = /^-?\d*\.?\d+(px|rem|em)$/;
const COLOUR_IN_SHADOW = /(rgba?\([^)]*\)|hsla?\([^)]*\)|oklab\([^)]*\)|oklch\([^)]*\)|#[0-9a-fA-F]{3,8})/;
const FAMILY_NAME = /^[A-Za-z0-9 _.-]+$/;
const SHADOW_CHARS =/^[\w\s.,()#%/-]+$/;

/** A plain CSS length such as "8px" or "0.5rem", or null. */
export function safeLength(value: string | undefined): string | null {
  const v = (value ?? "").trim();
  return LENGTH.test(v) ? v : null;
}

/** A single box-shadow made only of normal characters, or null. */
export function safeShadow(value: string | undefined): string | null {
  const v = (value ?? "").trim();
  return v.length > 0 && v.length <= 200 && SHADOW_CHARS.test(v) ? v : null;
}

/** A lower-case name safe to use as a key: letters, digits and dashes. */
export function slug(text: string): string {
  return text.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "") || "item";
}

/** Splits "0 4px 12px 0 rgba(0,0,0,.1)" or "rgba(0,0,0,.1) 0 4px 12px" into numbers and colour; null for several shadows or odd text. */
export function parseShadow(value: string): { x: string; y: string; blur: string; spread: string; color: string } | null {
  const shadow = safeShadow(value);
  if (!shadow || shadow.includes("inset")) return null;
  const colour = COLOUR_IN_SHADOW.exec(shadow)?.[0];
  if (!colour) return null;
  const rest = shadow.replace(colour, " ").trim();
  if (rest.includes(",")) return null;
  const parts = rest.split(/\s+/).filter(Boolean);
  if (parts.length < 2 || parts.length > 4 || !parts.every((p) => /^-?\d*\.?\d+(px)?$/.test(p))) return null;
  const px = (p: string | undefined) => String(parseFloat(p ?? "0") || 0);
  return { x: px(parts[0]), y: px(parts[1]), blur: px(parts[2]), spread: px(parts[3]), color: colour };
}

interface Named {
  key: string;
  value: string;
}

function colourNames(colors: readonly DesignColor[]): Named[] {
  const counts = new Map<string, number>();
  return colors.map((c) => {
    const role = slug(c.role ?? "color");
    const n = (counts.get(role) ?? 0) + 1;
    counts.set(role, n);
    return { key: `${role}-${n}`, value: c.hex.toLowerCase() };
  });
}

function numbered(values: readonly string[], prefix: string, keep: (v: string) => string | null): Named[] {
  const out: Named[] = [];
  for (const v of values) {
    const safe = keep(v);
    if (safe && !out.some((o) => o.value === safe)) out.push({ key: `${prefix}-${out.length + 1}`, value: safe });
  }
  return out;
}

function fontNames(design: DesignSystem): Named[] {
  const out: Named[] = [];
  for (const f of design.fonts ?? []) {
    const family = (f.family ?? "").trim();
    if (family.length > 0 && family.length <= 60 && FAMILY_NAME.test(family) && !out.some((o) => o.value === family)) {
      out.push({ key: slug(family), value: family });
    }
  }
  return out;
}

interface Tokens {
  colors: Named[];
  fonts: Named[];
  radii: Named[];
  spacing: Named[];
  shadows: Named[];
}

function collect(design: DesignSystem): Tokens {
  return {
    colors: colourNames(cleanColours(design.colors)),
    fonts: fontNames(design),
    radii: numbered(design.radii ?? [], "radius", safeLength),
    spacing: numbered(design.spacingScale ?? [], "space", safeLength),
    shadows: numbered(design.shadows ?? [], "shadow", (v) => (safeShadow(v) ? v.trim() : null)),
  };
}

/** Figma design tokens in the Tokens Studio JSON format (import with the free Tokens Studio plugin). */
export function toFigmaTokens(design: DesignSystem): string {
  const t = collect(design);
  const group = (items: Named[], type: string) => Object.fromEntries(items.map((i) => [i.key, { value: i.value, type }]));
  const set: Record<string, unknown> = {};
  if (t.colors.length) set.color = group(t.colors, "color");
  if (t.fonts.length) set.fontFamilies = group(t.fonts, "fontFamilies");
  if (t.radii.length) set.borderRadius = group(t.radii, "borderRadius");
  if (t.spacing.length) set.spacing = group(t.spacing, "spacing");
  const shadows = t.shadows.flatMap((s) => {
    const p = parseShadow(s.value);
    return p ? [[s.key, { value: { ...p, type: "dropShadow" }, type: "boxShadow" }] as const] : [];
  });
  if (shadows.length) set.boxShadow = Object.fromEntries(shadows);
  return JSON.stringify({ global: set, $themes: [], $metadata: { tokenSetOrder: ["global"] } }, null, 2) + "\n";
}

/** A `:root` block of CSS variables for the colours, fonts, radii, spacing and shadows. */
export function toCssVariables(design: DesignSystem): string {
  const t = collect(design);
  const lines: string[] = [];
  t.colors.forEach((c) => lines.push(`  --color-${c.key}: ${c.value};`));
  t.fonts.forEach((f) => lines.push(`  --font-${f.key}: ${fontFamilyCss(f.value).replace(/^font-family: /, "").replace(/;$/, "")};`));
  t.radii.forEach((r) => lines.push(`  --${r.key}: ${r.value};`));
  t.spacing.forEach((s) => lines.push(`  --${s.key}: ${s.value};`));
  t.shadows.forEach((s) => lines.push(`  --${s.key}: ${s.value};`));
  return `:root {\n${lines.join("\n")}\n}\n`;
}

/** A Tailwind (v3) config whose theme.extend holds the colours, fonts, radii, spacing and shadows. */
export function toTailwindConfig(design: DesignSystem): string {
  const t = collect(design);
  const map = (items: Named[], value: (i: Named) => unknown = (i) => i.value) =>
    Object.fromEntries(items.map((i) => [i.key, value(i)]));
  const extend: Record<string, unknown> = {};
  if (t.colors.length) extend.colors = map(t.colors);
  if (t.fonts.length) {
    extend.fontFamily = map(t.fonts, (f) => [f.value, fontFamilyCss(f.value).includes("monospace") ? "monospace" : "sans-serif"]);
  }
  if (t.radii.length) extend.borderRadius = map(t.radii);
  if (t.spacing.length) extend.spacing = map(t.spacing);
  if (t.shadows.length) extend.boxShadow = map(t.shadows);
  const body = JSON.stringify(extend, null, 2).split("\n").map((line, i) => (i === 0 ? line : "    " + line)).join("\n");
  return (
    "/** @type {import('tailwindcss').Config} */\n" +
    `module.exports = {\n  content: ["./src/**/*.{js,ts,jsx,tsx,html}"],\n  theme: {\n    extend: ${body},\n  },\n  plugins: [],\n};\n`
  );
}

/** True when there is anything to export. */
export function hasExportableDesign(design: DesignSystem | null | undefined): design is DesignSystem {
  if (!design) return false;
  const t = collect(design);
  return t.colors.length + t.fonts.length + t.radii.length + t.spacing.length + t.shadows.length > 0;
}

export const EXPORT_FORMATS: readonly ExportFormat[] = [
  {
    id: "figma",
    label: "Figma tokens",
    description: "Colours, fonts, radii, spacing and shadows as Tokens Studio JSON. Import it in Figma with the free Tokens Studio plugin.",
    filename: "design-tokens.json",
    mime: "application/json",
    build: toFigmaTokens,
  },
  {
    id: "css",
    label: "CSS variables",
    description: "A :root block you can paste into any stylesheet.",
    filename: "design-tokens.css",
    mime: "text/css",
    build: toCssVariables,
  },
  {
    id: "tailwind",
    label: "Tailwind config",
    description: "A tailwind.config.js (v3) with the design system under theme.extend.",
    filename: "tailwind.config.js",
    mime: "text/javascript",
    build: toTailwindConfig,
  },
];
