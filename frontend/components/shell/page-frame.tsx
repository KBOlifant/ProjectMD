import type { ReactNode } from "react";

// Reading column for the pages that are not full-bleed (History, Admin, Processing). Left-aligned
// with generous side padding, like the wireframes. `size` picks the width: "list" for row lists
// (960px), "narrow" for a single card, "wide" for the full width. Stores nothing.
const WIDTHS = { list: "max-w-[960px]", narrow: "max-w-[520px]", wide: "" } as const;

export function PageFrame({ children, size = "list" }: { children: ReactNode; size?: keyof typeof WIDTHS }) {
  return (
    <div className="w-full px-4 py-10 md:px-16 md:py-12">
      <div className={WIDTHS[size]}>{children}</div>
    </div>
  );
}

// Small plum label above a page heading (e.g. "Archive"), as in the wireframes. Stores nothing.
export function Eyebrow({ children }: { children: ReactNode }) {
  return <p className="ts-label text-[14px] leading-5 text-plum-300">{children}</p>;
}
