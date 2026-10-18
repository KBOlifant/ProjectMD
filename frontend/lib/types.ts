export interface Tech {
  name: string;
  highlighted?: boolean;
}

// One colour found on the site, with how often it is used and what job it does.
export interface DesignColor {
  hex: string;
  uses?: number;
  role?: string;
}

export interface DesignFont {
  family: string;
  weights?: number[];
  sizes?: string[];
}

export interface CssVariable {
  name: string;
  value: string;
}

// The captured design system (mirrors the backend DesignSystem record, camelCase). Every field may be missing.
export interface DesignSystem {
  colors?: DesignColor[];
  fonts?: DesignFont[];
  radii?: string[];
  shadows?: string[];
  spacingScale?: string[];
  cssVariables?: CssVariable[];
  breakpoints?: string[];
  utilityFramework?: string | null;
}

// One AI-written, server-cleaned HTML/CSS version of a component (mirrors the backend ComponentPreview record).
export interface ComponentPreview {
  kind: string;
  name: string;
  html: string;
  css: string;
}

export interface ComponentPreviews {
  items: ComponentPreview[];
}

// Extra analysis stored with a scan. Later features add more keys; unknown keys are ignored. `api` is not shown yet.
export interface ScanAnalysis {
  version?: number;
  design?: DesignSystem | null;
  api?: unknown;
  components?: ComponentPreviews | null;
}

export interface Scan {
  id: string;
  url: string;
  host: string;
  createdAt: string;
  durationMs: number;
  confidence: number;
  pages: number;
  tech: Tech[];
  markdown: string;
  analysis?: ScanAnalysis | null;
}
