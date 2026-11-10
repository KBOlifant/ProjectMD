"use client";

import { useInView } from "framer-motion";
import { useEffect, useRef, useState } from "react";

const GLYPHS = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
const MS_PER_CHAR = 20;

const scramble = (text: string, revealed: number) =>
  [...text]
    .map((c, i) => (i < revealed || c === " " ? c : GLYPHS[Math.floor(Math.random() * GLYPHS.length)]))
    .join("");

// Types the text out left to right: letters not yet reached cycle through random glyphs, then
// lock in. Starts once on first view. The real text is in aria-label and is what renders with
// reduced motion or before the effect runs, so SSR/hydration never sees random characters.
export function ScrambleText({ text, delay = 0, className }: { text: string; delay?: number; className?: string }) {
  const ref = useRef<HTMLSpanElement>(null);
  const inView = useInView(ref, { once: true });
  const [shown, setShown] = useState(text);

  useEffect(() => {
    if (!inView || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    let frame = 0;
    let start: number | null = null;
    const tick = (now: number) => {
      start ??= now;
      const revealed = Math.floor((now - start) / MS_PER_CHAR);
      setShown(scramble(text, revealed));
      if (revealed < text.length) frame = requestAnimationFrame(tick);
    };
    const timer = setTimeout(() => (frame = requestAnimationFrame(tick)), delay);
    return () => {
      clearTimeout(timer);
      cancelAnimationFrame(frame);
      setShown(text);
    };
  }, [inView, text, delay]);

  return (
    <span ref={ref} aria-label={text} className={className}>
      <span aria-hidden="true">{shown}</span>
    </span>
  );
}
