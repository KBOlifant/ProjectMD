import type { DesignColor, DesignFont } from "./types";

const HEX = /^#([0-9a-f]{3}|[0-9a-f]{4}|[0-9a-f]{6}|[0-9a-f]{8})$/i;
const GENERIC_FAMILIES = new Set([
  "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "ui-serif", "ui-sans-serif",
  "ui-monospace", "ui-rounded", "emoji", "math", "fangsong", "inherit", "initial", "unset",
]);
const FAMILY_PATTERN = /^[A-Za-z0-9 ]{2,40}$/;
const MAIN_COLOUR_LIMIT = 5;
const NEAR_DUPLICATE_DISTANCE = 40;

// Turns "#abc" / "#aabbcc" / "#aabbccdd" into [r, g, b] (alpha ignored), or null if it is not a valid hex.
export function parseHex(hex: string): [number, number, number] | null {
  if (!HEX.test(hex)) return null;
  let h = hex.slice(1);
  if (h.length <= 4) h = [...h].map((c) => c + c).join("");
  return [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16)) as [number, number, number];
}

// Picks black or white text, whichever has the better contrast on the given background colour.
export function readableTextOn(hex: string): string {
  const rgb = parseHex(hex);
  if (!rgb) return "#ffffff";
  const [r, g, b] = rgb.map((v) => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  });
  const luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
  return luminance > 0.179 ? "#000000" : "#ffffff";
}

function distance(a: string, b: string): number {
  const x = parseHex(a);
  const y = parseHex(b);
  if (!x || !y) return Infinity;
  return Math.hypot(x[0] - y[0], x[1] - y[1], x[2] - y[2]);
}

// Keeps only colours with a valid hex, lowest-case, without repeats. Does not change the input.
export function cleanColours(colors: readonly DesignColor[] | undefined): DesignColor[] {
  const seen = new Set<string>();
  const out: DesignColor[] = [];
  for (const c of colors ?? []) {
    if (!c || typeof c.hex !== "string" || !HEX.test(c.hex)) continue;
    const hex = c.hex.toLowerCase();
    if (seen.has(hex)) continue;
    seen.add(hex);
    out.push({ ...c, hex });
  }
  return out;
}

// Splits colours into 3-5 main ones (most used, one of each role first, no near-duplicates) and the rest.
export function pickMainColours(colors: readonly DesignColor[]): { main: DesignColor[]; others: DesignColor[] } {
  const sorted = [...colors].sort((a, b) => (b.uses ?? 0) - (a.uses ?? 0));
  const main: DesignColor[] = [];
  const isFree = (c: DesignColor) =>
    !main.includes(c) && main.every((m) => distance(m.hex, c.hex) > NEAR_DUPLICATE_DISTANCE);
  const roles = new Set<string>();
  for (const c of sorted) {
    if (main.length >= MAIN_COLOUR_LIMIT) break;
    if (c.role && !roles.has(c.role) && isFree(c)) {
      roles.add(c.role);
      main.push(c);
    }
  }
  for (const c of sorted) {
    if (main.length >= Math.min(3, sorted.length)) break;
    if (isFree(c)) main.push(c);
  }
  for (const c of sorted) {
    if (main.length >= 3) break;
    if (!main.includes(c)) main.push(c);
  }
  main.sort((a, b) => (b.uses ?? 0) - (a.uses ?? 0));
  return { main, others: sorted.filter((c) => !main.includes(c)) };
}

// True when the family can be requested from Google Fonts (valid characters and not a generic family).
export function isGoogleFontCandidate(family: string): boolean {
  return FAMILY_PATTERN.test(family) && !GENERIC_FAMILIES.has(family.toLowerCase());
}

// Cleans a weight list to unique whole numbers between 100 and 900, sorted. Defaults to [400].
export function cleanWeights(weights: readonly number[] | undefined): number[] {
  const w = [...new Set((weights ?? []).filter((n) => Number.isInteger(n) && n >= 100 && n <= 900))];
  return w.length ? w.sort((a, b) => a - b) : [400];
}

// Builds the Google Fonts CSS URL for one family and its weights (display=swap). Null if the family is not valid.
export function googleFontsUrl(font: DesignFont): string | null {
  if (!isGoogleFontCandidate(font.family)) return null;
  const family = encodeURIComponent(font.family.trim()).replace(/%20/g, "+");
  return `https://fonts.googleapis.com/css2?family=${family}:wght@${cleanWeights(font.weights).join(";")}&display=swap`;
}

// The CSS `font-family` declaration for a family, with a generic fallback.
export function fontFamilyCss(family: string): string {
  const generic = /mono|code|courier/i.test(family) ? "monospace" : /serif/i.test(family) && !/sans/i.test(family) ? "serif" : "sans-serif";
  const name = /^[A-Za-z0-9_-]+$/.test(family) ? family : `"${family.replace(/["\\]/g, "")}"`;
  return `font-family: ${name}, ${generic};`;
}

// Builds a `:root { ... }` block from CSS variables, skipping names or values that could break out of the block.
export function rootBlock(vars: readonly { name: string; value: string }[]): string {
  const lines = vars
    .filter((v) => /^--[A-Za-z0-9_-]+$/.test(v.name) && !/[;{}<>]/.test(v.value))
    .map((v) => `  ${v.name}: ${v.value};`);
  return `:root {\n${lines.join("\n")}\n}`;
}
