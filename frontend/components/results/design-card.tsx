"use client";

import { motion } from "framer-motion";
import type { ReactNode } from "react";
import { STAGGER_ITEM } from "@/lib/motion";

interface DesignCardProps {
  id: string;
  title: string;
  // Right-aligned count, e.g. "8 values".
  meta?: string;
  children: ReactNode;
  className?: string;
  // Takes a full row instead of sharing one with other tiles.
  wide?: boolean;
}

// Bordered tile with a title row, used for each section of the Design tab (Colours, Fonts, Corner
// radii...). `id` ties the heading to the section for screen readers. Stores nothing.
export function DesignCard({ id, title, meta, children, className = "", wide = false }: DesignCardProps) {
  return (
    <motion.section variants={STAGGER_ITEM} aria-labelledby={id} className={`${wide ? "basis-full" : "min-w-0 flex-[1_1_260px]"} rounded-2xl border border-border-subtle bg-bg-surface p-5 ${className}`}>
      <header className="mb-4 flex items-baseline justify-between gap-3">
        <h3 id={id} className="ts-h3 text-ink">
          {title}
        </h3>
        {meta && <span className="font-mono text-[10px] text-ink-muted">{meta}</span>}
      </header>
      {children}
    </motion.section>
  );
}
