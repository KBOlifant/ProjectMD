"use client";

import { useEffect } from "react";
import "locomotive-scroll/locomotive-scroll.css";

// Turns on Locomotive Scroll (smooth, eased page scrolling plus `data-scroll-speed` parallax) for
// as long as it is mounted, and tears it down on unmount so other pages scroll normally. Skipped
// for people who ask for reduced motion. Renders nothing.
export function SmoothScroll() {
  useEffect(() => {
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    let scroll: { destroy: () => void } | null = null;
    let cancelled = false;
    // Imported on the client only: the library touches window as soon as it loads.
    void import("locomotive-scroll").then(({ default: LocomotiveScroll }) => {
      if (!cancelled) scroll = new LocomotiveScroll();
    });
    return () => {
      cancelled = true;
      scroll?.destroy();
    };
  }, []);
  return null;
}
