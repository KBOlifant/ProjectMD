import type { ScanAnalysis } from "@/lib/types";

// MOCK design data in the same shape the backend sends (Scan.analysis), so the Design tab works in
// static mode without a server. Fixed stand-in values, not measured from any site.
export const MOCK_ANALYSIS: ScanAnalysis = {
  version: 1,
  design: {
    colors: [
      { hex: "#0f172a", uses: 120, role: "text" },
      { hex: "#ffffff", uses: 96, role: "background" },
      { hex: "#38bdf8", uses: 41, role: "accent" },
      { hex: "#e2e8f0", uses: 33, role: "border" },
      { hex: "#f1f5f9", uses: 18, role: "background" },
      { hex: "#0ea5e9", uses: 12, role: "accent" },
      { hex: "#64748b", uses: 9, role: "text" },
      { hex: "#f43f5e", uses: 4, role: "accent" },
    ],
    fonts: [
      { family: "Inter", weights: [400, 500, 600, 700], sizes: ["12px", "14px", "16px", "20px", "30px"] },
      { family: "Fira Code", weights: [400, 500], sizes: ["13px", "14px"] },
      { family: "Acme Private Sans", weights: [400], sizes: ["16px"] },
    ],
    radii: ["4px", "8px", "12px", "9999px"],
    shadows: ["0 1px 2px #0003", "0 4px 12px rgba(0,0,0,0.15)", "0 20px 40px rgba(15,23,42,0.25)"],
    spacingScale: ["4px", "8px", "12px", "16px", "24px", "32px", "48px"],
    cssVariables: [
      { name: "--brand", value: "#38bdf8" },
      { name: "--ink", value: "#0f172a" },
      { name: "--radius", value: "8px" },
    ],
    breakpoints: ["640px", "768px", "1024px", "1280px"],
    utilityFramework: "Tailwind",
  },
  api: null,
  animations: {
    summary: "Sample data: a calm page with a headline reveal on load and cards that fade in as you scroll.",
    libraries: ["GSAP", "ScrollTrigger"],
    items: [
      {
        section: "Hero",
        name: "Headline reveal",
        description: "The headline slides up about 40 pixels while fading in as the page loads.",
        trigger: "load",
        evidence: "measured",
        library: "GSAP",
        recreate: "Tween opacity from 0 and y from 40 over about 0.6 seconds.",
        estimate: "GSAP",
        gsap: 'gsap.from(".target", {\n  opacity: 0,\n  y: 40,\n  duration: 0.6,\n  ease: "power2.out",\n});',
        framerMotion:
          '<motion.div\n  initial={{ opacity: 0, y: 40 }}\n  animate={{ opacity: 1, y: 0 }}\n  transition={{ duration: 0.6, ease: "easeOut" }}\n/>',
      },
      {
        section: "Features",
        name: "Card fade-in",
        description: "Each card fades in and rises slightly when it scrolls into view.",
        trigger: "scroll",
        evidence: "inferred",
        library: "",
        recreate: "Animate opacity and y when the card enters the viewport.",
        estimate: "GSAP",
        gsap: 'gsap.registerPlugin(ScrollTrigger);\n\ngsap.from(".target", {\n  opacity: 0,\n  y: 40,\n  duration: 0.6,\n  ease: "power2.out",\n  scrollTrigger: { trigger: ".target", start: "top 80%" },\n});',
        framerMotion:
          '<motion.div\n  initial={{ opacity: 0, y: 40 }}\n  whileInView={{ opacity: 1, y: 0 }}\n  viewport={{ once: true, amount: 0.3 }}\n  transition={{ duration: 0.6, ease: "easeOut" }}\n/>',
      },
    ],
  },
  components: {
    items: [
      {
        kind: "button",
        name: "Primary button",
        html: '<button class="btn">Get started</button>',
        css: ".btn { color: #ffffff; background-color: #0ea5e9; border-radius: 8px; padding: 10px 20px; font-family: Inter, sans-serif; font-size: 14px; font-weight: 600; border: 0; cursor: pointer; }\n.btn:hover { background-color: #0284c7; }",
      },
      {
        kind: "input",
        name: "Text input",
        html: '<input class="field" type="text" placeholder="Search docs">',
        css: ".field { color: #0f172a; background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 8px; padding: 10px 14px; font-family: Inter, sans-serif; font-size: 14px; width: 220px; }\n.field:focus { border-color: #38bdf8; outline: 2px solid #bae6fd; }",
      },
      {
        kind: "card",
        name: "Card",
        html: '<div class="card"><h3 class="title">Card title</h3><p class="text">A short description of the card.</p></div>',
        css: ".card { background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px; padding: 20px; box-shadow: 0 4px 12px rgba(0,0,0,0.1); width: 240px; font-family: Inter, sans-serif; }\n.title { color: #0f172a; font-size: 18px; margin: 0 0 8px; }\n.text { color: #64748b; font-size: 14px; margin: 0; }",
      },
    ],
  },
};
