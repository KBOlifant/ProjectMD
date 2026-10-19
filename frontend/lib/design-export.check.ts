// Runnable check for the design export (Figma tokens, CSS variables, Tailwind config).
// Run from frontend/ with: npx tsx lib/design-export.check.ts
// It touches only pure modules and exits non-zero on any failure.
import assert from "node:assert/strict";
import { EXPORT_FORMATS, hasExportableDesign, parseShadow, safeLength, safeShadow, toCssVariables, toFigmaTokens, toTailwindConfig } from "./design-export";
import type { DesignSystem } from "./types";

const design: DesignSystem = {
  colors: [
    { hex: "#0F172A", uses: 120, role: "text" },
    { hex: "#ffffff", uses: 96, role: "background" },
    { hex: "#38bdf8", uses: 41, role: "accent" },
    { hex: "#f43f5e", uses: 4, role: "accent" },
    { hex: "red; } body { display:none", uses: 1, role: "text" },
  ],
  fonts: [
    { family: "Inter", weights: [400, 700] },
    { family: 'Evil"; } * { x:y', weights: [400] },
  ],
  radii: ["8px", "9999px", "8px", "url(x)"],
  spacingScale: ["4px", "8px", "calc(1px + 2px)"],
  shadows: ["0 4px 12px rgba(0,0,0,0.15)", "rgba(0, 0, 0, 0.1) 0px 4px 6px -1px", "0 1px 2px #0003, 0 2px 4px #0003", "url(http://evil)"],
};

let failures = 0;
function check(name: string, run: () => void) {
  try {
    run();
    console.log(`PASS ${name}`);
  } catch (e) {
    failures++;
    console.log(`FAIL ${name}: ${(e as Error).message}`);
  }
}

check("safeLength and safeShadow accept plain values only", () => {
  assert.equal(safeLength(" 8px "), "8px");
  assert.equal(safeLength("calc(1px + 2px)"), null);
  assert.equal(safeShadow("0 1px 2px #0003"), "0 1px 2px #0003");
  assert.equal(safeShadow("url(http://evil)"), null);
});

check("parseShadow reads both orders and rejects several shadows", () => {
  assert.deepEqual(parseShadow("0 4px 12px rgba(0,0,0,0.15)"), { x: "0", y: "4", blur: "12", spread: "0", color: "rgba(0,0,0,0.15)" });
  assert.deepEqual(parseShadow("rgba(0, 0, 0, 0.1) 0px 4px 6px -1px"), { x: "0", y: "4", blur: "6", spread: "-1", color: "rgba(0, 0, 0, 0.1)" });
  assert.equal(parseShadow("0 1px 2px #0003, 0 2px 4px #0003"), null);
  assert.equal(parseShadow("inset 0 1px 2px #0003"), null);
});

check("Figma tokens have typed groups, role names and no unsafe values", () => {
  const json = JSON.parse(toFigmaTokens(design));
  const set = json.global;
  assert.deepEqual(json.$metadata.tokenSetOrder, ["global"]);
  assert.deepEqual(set.color["text-1"], { value: "#0f172a", type: "color" });
  assert.equal(set.color["accent-2"].value, "#f43f5e");
  assert.equal(Object.keys(set.color).length, 4);
  assert.deepEqual(Object.keys(set.borderRadius), ["radius-1", "radius-2"]);
  assert.deepEqual(set.spacing, { "space-1": { value: "4px", type: "spacing" }, "space-2": { value: "8px", type: "spacing" } });
  assert.equal(set.boxShadow["shadow-1"].value.blur, "12");
  assert.equal(set.boxShadow["shadow-2"].value.spread, "-1");
  assert.equal(Object.keys(set.boxShadow).length, 2);
  assert.equal(set.fontFamilies.inter.value, "Inter");
});

check("CSS variables are one safe :root block", () => {
  const css = toCssVariables(design);
  assert.match(css, /^:root \{\n/);
  assert.match(css, /--color-text-1: #0f172a;/);
  assert.match(css, /--font-inter: Inter, sans-serif;/);
  assert.match(css, /--radius-2: 9999px;/);
  assert.doesNotMatch(css, /evil|body|display:none|url\(|calc/i);
  assert.equal(css.split("{").length, 2);
});

check("Tailwind config is valid JavaScript with the design under theme.extend", () => {
  const source = toTailwindConfig(design);
  const module_ = { exports: {} as { theme: { extend: Record<string, Record<string, unknown>> } } };
  new Function("module", source)(module_);
  const extend = module_.exports.theme.extend;
  assert.equal(extend.colors["accent-1"], "#38bdf8");
  assert.deepEqual(extend.fontFamily.inter, ["Inter", "sans-serif"]);
  assert.equal(extend.borderRadius["radius-1"], "8px");
  assert.doesNotMatch(source, /evil|url\(|calc/i);
});

check("an empty design exports nothing and is reported as empty", () => {
  assert.equal(hasExportableDesign({}), false);
  assert.equal(hasExportableDesign(null), false);
  assert.equal(hasExportableDesign(design), true);
  for (const f of EXPORT_FORMATS) assert.ok(f.build({}).length > 0, f.id);
});

check("every format has a file name and type", () => {
  assert.deepEqual(EXPORT_FORMATS.map((f) => f.id), ["figma", "css", "tailwind"]);
  for (const f of EXPORT_FORMATS) assert.ok(f.filename.includes(".") && f.mime.includes("/"));
});

if (failures > 0) {
  console.log(`${failures} check(s) failed`);
  process.exit(1);
}
console.log("All design export checks passed");
