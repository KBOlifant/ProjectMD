"use client";

import { motion, useReducedMotion } from "framer-motion";

const COLUMNS = 24;
const ROWS = 14;
const BREATH_SECONDS = 4.6; // one full in-and-out
const WAVE_DELAY = 0.07; // seconds of lag per cell of distance from the centre
const CENTRE_HUE = 258; // violet-500
const HUE_SHIFT = 22; // degrees toward blue on the left and pink on the right

const CELLS = Array.from({ length: COLUMNS * ROWS }, (_, i) => {
  const dx = (i % COLUMNS) - (COLUMNS - 1) / 2;
  const dy = Math.floor(i / COLUMNS) - (ROWS - 1) / 2;
  const hue = CENTRE_HUE + (dx / ((COLUMNS - 1) / 2)) * HUE_SHIFT;
  return { id: i, delay: Math.hypot(dx, dy) * WAVE_DELAY, colour: `hsl(${hue} 90% 66% / 0.25)` };
});

// Full-bleed grid behind the Processing card that "breathes": every cell shrinks and fades then
// swells back, offset by its distance from the centre so the pulse ripples out in a loop. Purely
// decorative (hidden from assistive tech, ignores the pointer) and still when the visitor prefers
// reduced motion. Stores nothing.
export function GridBreath() {
  const reduceMotion = useReducedMotion();
  return (
    <div
      aria-hidden="true"
      className="pointer-events-none absolute inset-0 -z-10 grid gap-1.5 p-6 [mask-image:radial-gradient(ellipse_at_center,black_25%,transparent_80%)]"
      style={{ gridTemplateColumns: `repeat(${COLUMNS}, 1fr)`, gridTemplateRows: `repeat(${ROWS}, 1fr)` }}
    >
      {CELLS.map((cell) => (
        <motion.div
          key={cell.id}
          className="rounded-[3px]"
          style={{ backgroundColor: cell.colour }}
          initial={{ scale: 1, opacity: 0.9 }}
          animate={reduceMotion ? undefined : { scale: [1, 0.4, 1], opacity: [0.9, 0.25, 0.9] }}
          transition={{ duration: BREATH_SECONDS, delay: cell.delay, ease: "easeInOut", repeat: Infinity }}
        />
      ))}
    </div>
  );
}
