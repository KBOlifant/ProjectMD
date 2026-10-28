"use client";

import { motion } from "framer-motion";
import type { AnimationItem } from "@/lib/types";

export type PreviewKind =
  | "locomotive" | "blur" | "stagger" | "marquee" | "draw" | "text" | "clip"
  | "flip" | "tilt" | "pulse" | "scale" | "spin" | "slide" | "fade" | "jump";

// First match wins, so specific effects ("staggered fade") sit above generic ones ("fade").
const RULES: readonly [PreviewKind, RegExp][] = [
  ["locomotive", /locomotive|parallax|smooth.?scroll|inertia/],
  ["blur", /blur/],
  ["stagger", /stagger|cascade|sequen/],
  ["marquee", /marquee|ticker|infinite scroll/],
  ["draw", /draw|stroke|svg path|line.?draw/],
  ["text", /typewriter|typing|scramble|letter|per.?word|split.?text|text reveal/],
  ["clip", /clip|mask|wipe/],
  ["flip", /flip|rotate[xy]|3d/],
  ["tilt", /tilt|magnetic|cursor|follow/],
  ["pulse", /pulse|breath|glow|heartbeat/],
  ["scale", /scale|zoom|grow|shrink|pop/],
  ["spin", /spin|rotat/],
  ["slide", /slide|translate|fly|move|drift|reveal|rise/],
  ["fade", /fade|opacity/],
];

// Picks a canned preview from text the AI already gave us for this animation (name, library hint,
// description, rebuild note, estimate) plus the libraries measured on the page. Falls back to the
// jumping outlined cubes.
export function previewKind(item: AnimationItem, libraries: readonly string[]): PreviewKind {
  const text = `${item.name} ${item.library} ${item.description} ${item.recreate} ${item.estimate}`.toLowerCase();
  const hit = RULES.find(([, pattern]) => pattern.test(text));
  if (hit) return hit[0];
  return /locomotive/.test(libraries.join(" ").toLowerCase()) ? "locomotive" : "jump";
}

const LOOP = { repeat: Infinity, ease: "easeInOut" as const };
const SQUARE = "size-8 rounded-md bg-violet-500";

function Row({ children }: { children: React.ReactNode }) {
  return <div className="flex items-center gap-3">{children}</div>;
}

// A small looping preview that stands in for the real captured animation. Decorative only.
export function Preview({ kind }: { kind: PreviewKind }) {
  switch (kind) {
    case "locomotive":
      return (
        <div className="flex h-16 items-end gap-2 overflow-hidden">
          {[0, 1, 2].map((i) => (
            <motion.span key={i} className="w-3 rounded-sm bg-violet-500" style={{ height: 14 + i * 10 }}
              animate={{ y: [24, -24] }} transition={{ ...LOOP, duration: 1.8, delay: i * 0.2 }} />
          ))}
        </div>
      );
    case "blur":
      return <motion.div className="size-10 rounded-md bg-violet-500" animate={{ filter: ["blur(0px)", "blur(10px)", "blur(0px)"] }} transition={{ ...LOOP, duration: 2.4 }} />;
    case "stagger":
      return (
        <Row>
          {[0, 1, 2, 3].map((i) => (
            <motion.div key={i} className="size-6 rounded-md bg-violet-500" animate={{ opacity: [0, 1, 1, 0], y: [12, 0, 0, -12] }}
              transition={{ ...LOOP, duration: 2.4, delay: i * 0.2, times: [0, 0.25, 0.75, 1] }} />
          ))}
        </Row>
      );
    case "marquee":
      return (
        <div className="w-40 overflow-hidden">
          <motion.div className="flex w-max gap-3" animate={{ x: [0, -60] }} transition={{ repeat: Infinity, ease: "linear", duration: 1.6 }}>
            {Array.from({ length: 10 }, (_, i) => <span key={i} className="size-6 flex-none rounded-md bg-violet-500" />)}
          </motion.div>
        </div>
      );
    case "draw":
      return (
        <svg width="72" height="40" viewBox="0 0 72 40" fill="none" className="text-violet-500">
          <motion.path d="M4 32 C 18 -4, 30 44, 44 8 S 64 20, 68 6" stroke="currentColor" strokeWidth="3" strokeLinecap="round"
            animate={{ pathLength: [0, 1, 1], opacity: [1, 1, 0] }} transition={{ ...LOOP, duration: 2.4, times: [0, 0.6, 1] }} />
        </svg>
      );
    case "text":
      return (
        <div className="space-y-2">
          {[64, 44].map((w, i) => (
            <motion.div key={w} className="h-2 rounded-full bg-violet-500" style={{ width: w, originX: 0 }}
              animate={{ scaleX: [0, 1, 1, 0] }} transition={{ ...LOOP, duration: 2.4, delay: i * 0.3, times: [0, 0.4, 0.8, 1] }} />
          ))}
        </div>
      );
    case "clip":
      return <motion.div className={SQUARE} animate={{ clipPath: ["inset(0 100% 0 0)", "inset(0 0% 0 0)", "inset(0 0% 0 0)", "inset(0 0 0 100%)"] }} transition={{ ...LOOP, duration: 2.6, times: [0, 0.4, 0.7, 1] }} />;
    case "flip":
      return <motion.div className={SQUARE} style={{ transformPerspective: 400 }} animate={{ rotateY: [0, 180, 360] }} transition={{ ...LOOP, duration: 2.6 }} />;
    case "tilt":
      return <motion.div className={SQUARE} style={{ transformPerspective: 400 }} animate={{ rotateX: [0, 22, 0, -22, 0], rotateY: [0, -22, 22, 0, 0] }} transition={{ ...LOOP, duration: 3 }} />;
    case "pulse":
      return <motion.div className={SQUARE} animate={{ scale: [1, 1.25, 1], boxShadow: ["0 0 0 0 rgb(139 92 246 / 0.5)", "0 0 0 14px rgb(139 92 246 / 0)", "0 0 0 0 rgb(139 92 246 / 0)"] }} transition={{ ...LOOP, duration: 1.8 }} />;
    case "scale":
      return <motion.div className={SQUARE} animate={{ scale: [0.4, 1.3, 0.4] }} transition={{ ...LOOP, duration: 2.2 }} />;
    case "spin":
      return <motion.div className={SQUARE} animate={{ rotate: [0, 360] }} transition={{ repeat: Infinity, ease: "linear", duration: 2.4 }} />;
    case "slide":
      return <motion.div className={SQUARE} animate={{ x: [-70, 70, -70] }} transition={{ ...LOOP, duration: 3 }} />;
    case "fade":
      return <motion.div className={SQUARE} animate={{ opacity: [0, 1, 0] }} transition={{ ...LOOP, duration: 2.4 }} />;
    default:
      return (
        <Row>
          {[0, 1, 2].map((i) => (
            <motion.div key={i} className="size-7 rounded-md border-2 border-violet-500" animate={{ y: [0, -22, 0] }}
              transition={{ ...LOOP, duration: 0.9, delay: i * 0.15, ease: "easeOut" }} />
          ))}
        </Row>
      );
  }
}
