"use client";

import { animate, useInView, useReducedMotion } from "framer-motion";
import { useEffect, useRef, useState } from "react";

export interface Stat {
  value: string;
  caption: string;
}

const COUNT_SECONDS = 1.8;
// A custom cubic-bezier (fast start, long soft landing) instead of a named easing keyword.
const COUNT_BEZIER: [number, number, number, number] = [0.22, 1, 0.36, 1];

// Splits "12", "85%" or "4.2s" into its number, decimal places and trailing text. Null for
// anything without a leading number (such as the "—" shown before there are scans).
function parseStat(value: string) {
  const match = /^(\d+(?:\.(\d+))?)(.*)$/.exec(value);
  return match ? { target: Number(match[1]), decimals: match[2]?.length ?? 0, suffix: match[3] } : null;
}

// A stat value that counts up from 0 to its real value once `active`, along COUNT_BEZIER. Values
// that are not numbers are shown as they are. Restarts if the value changes (history finished loading).
function CountUp({ value, active }: { value: string; active: boolean }) {
  const parsed = parseStat(value);
  const reduceMotion = useReducedMotion();
  const [fraction, setFraction] = useState(0);

  useEffect(() => {
    if (!active) return;
    const controls = animate(0, 1, {
      duration: reduceMotion ? 0 : COUNT_SECONDS,
      ease: COUNT_BEZIER,
      onUpdate: setFraction,
    });
    return () => controls.stop();
  }, [active, value, reduceMotion]);

  if (!parsed) return value;
  return `${(parsed.target * fraction).toFixed(parsed.decimals)}${parsed.suffix}`;
}

// Row of summary numbers (e.g. Total scans, Avg confidence) with captions. When it scrolls into view
// its shadow grows from nothing (CSS transition, see .stat-tile-shadow) and the numbers count up.
// Display only; stores nothing.
export function StatStrip({ stats }: { stats: Stat[] }) {
  const ref = useRef<HTMLDListElement>(null);
  const inView = useInView(ref, { once: true, amount: 0.5 });
  return (
    <dl
      ref={ref}
      className={`my-24 grid grid-cols-2 overflow-hidden rounded-[2.5rem] border border-border-subtle bg-bg-surface lg:grid-cols-4 lg:rounded-pill ${
        inView ? "stat-tile-shadow" : "stat-tile-shadow-off"
      }`}
    >
      {stats.map((s, i) => (
        <div
          key={s.caption}
          className={`px-8 py-8 text-center ${i % 2 === 1 ? "border-l border-border-subtle" : ""} ${
            i > 0 ? "lg:border-l lg:border-border-subtle" : ""
          } ${i >= 2 ? "border-t border-border-subtle lg:border-t-0" : ""}`}
        >
          <dd className="ts-stat text-ink text-4xl tabular-nums">
            <CountUp value={s.value} active={inView} />
          </dd>
          <dt className="ts-small text-ink-muted">{s.caption}</dt>
        </div>
      ))}
    </dl>
  );
}
