import type { Scan, Tech } from "@/lib/types";
import { TOTAL_MS } from "@/lib/scan";
import { MOCK_ANALYSIS } from "./mock-analysis";
import { buildMockMarkdown } from "./mock-markdown";
import { MOCK_STACKS } from "./mock-stacks";

// Turns text into a repeatable number (FNV-1a hash) so the same host always gets the same mock data. Stores nothing.
function hash(text: string): number {
  let h = 2166136261;
  for (let i = 0; i < text.length; i++) h = Math.imul(h ^ text.charCodeAt(i), 16777619);
  return h >>> 0;
}

// MOCK scan: builds a complete Scan object for a URL (id, url, host, date, duration, confidence,
// pages, tech list, markdown text) from the hostname hash. Makes no network request and uses no
// AI; every value is deterministic stand-in data, not a measurement. The caller (ScanProvider)
// saves the returned Scan to history; this function stores nothing itself.
export function buildScan(url: string): Scan {
  const host = new URL(url).hostname.replace(/^www\./, "");
  const h = hash(host);
  const stack = MOCK_STACKS[h % MOCK_STACKS.length];
  const tech: Tech[] = stack.map(([, name], i) => ({ name, highlighted: i < 3 }));
  const confidence = 88 + (h % 12);
  const pages = 4 + (h % 20);
  const createdAt = new Date().toISOString();
  const markdown = buildMockMarkdown({ host, url, createdAt, pages, confidence, stack });

  return {
    id: crypto.randomUUID(),
    url,
    host,
    createdAt,
    durationMs: TOTAL_MS,
    confidence,
    pages,
    tech,
    markdown,
    analysis: MOCK_ANALYSIS,
  };
}
