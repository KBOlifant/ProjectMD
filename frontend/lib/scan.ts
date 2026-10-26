// Pipeline step definitions and progress maths for the Processing screen. Scan DATA now comes
// from `scanProvider.generate()` in `app/functions/data/scan-provider.ts` (static mock today,
// backend API later). This file must not import anything from `app/functions/data`.
// Naming note: `components/scan-provider.tsx` is the React context for the in-progress scan;
// `app/functions/data/scan-provider.ts` is the data source.

export interface PipelineStep {
  id: string;
  title: string;
  active: string;
  done: string;
  ms: number;
}

export const PIPELINE_STEPS: PipelineStep[] = [
  { id: "fetch", title: "Fetching website", active: "Resolving the host and downloading the page…", done: "Page downloaded", ms: 300 },
  { id: "render", title: "Rendering client-side code", active: "Executing page scripts…", done: "Page rendered", ms: 600 },
  { id: "fingerprint", title: "Fingerprinting design and stack", active: "Inspecting tokens, components and the tech stack…", done: "Stack identified", ms: 900 },
  { id: "synthesize", title: "Writing documentation", active: "Writing the Markdown…", done: "Documentation written", ms: 1200 },
];

export const TOTAL_MS = PIPELINE_STEPS.reduce((sum, s) => sum + s.ms, 0);

export type StepState = "done" | "current" | "queued";

// Given how many ms have elapsed, returns the state ("done" | "current" | "queued") of each
// pipeline step in order, for the progress card. Pure calculation; stores nothing.
export function stepStates(elapsed: number): StepState[] {
  let start = 0;
  return PIPELINE_STEPS.map((s) => {
    const end = start + s.ms;
    const state: StepState = elapsed >= end ? "done" : elapsed >= start ? "current" : "queued";
    start = end;
    return state;
  });
}
