export interface Tech {
  name: string;
  highlighted?: boolean;
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
}
