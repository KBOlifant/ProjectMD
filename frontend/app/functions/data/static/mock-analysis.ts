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
};
