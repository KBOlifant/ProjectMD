// Progress maths for the Processing screen. The real work (static mock or the backend AI call)
// runs in the background with no known duration, so the bar is a single faked ramp: it eases
// toward FAKE_CEILING while waiting and only reaches 100 when the result actually arrives (see
// `components/scan-provider.tsx`).
// Naming note: `components/scan-provider.tsx` is the React context for the in-progress scan;
// `app/functions/data/scan-provider.ts` is the data source.

// The mock provider's reported scan duration (static mode has no real timing to report).
export const MOCK_DURATION_MS = 3000;

// The bar never reaches 100 on its own; it waits here for the real result.
export const FAKE_CEILING = 90;
// How long the ease-toward-the-ceiling takes to feel "gradual" (not a hard duration: the curve
// approaches but never reaches the ceiling, see easeTowardCeiling).
const FAKE_RAMP_MS = 4000;

// Eases 0 toward FAKE_CEILING as elapsedMs grows, approaching it asymptotically so the bar keeps
// drifting (slower and slower) for as long as the real call takes. Pure calculation; stores nothing.
export function easeTowardCeiling(elapsedMs: number): number {
  return FAKE_CEILING * (1 - Math.exp(-elapsedMs / FAKE_RAMP_MS));
}
