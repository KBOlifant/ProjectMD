"use client";

import { AnimatePresence, motion } from "framer-motion";
import Link from "next/link";
import { formatDate, formatSeconds, initials } from "@/lib/format";
import type { Scan } from "@/lib/types";
import { TechTag } from "./tech-chip";

const DISSOLVE_SECONDS = 0.35;

// Large screenshot of the history row being hovered, shown beside the list on wide screens. When
// the hovered row changes the old image dissolves out while the next one fades in on top of it.
// The host and the scan's metadata sit over the bottom of the image, and the whole card opens the
// scan's results. Hidden (and not clickable) while no row is hovered. Purely a mouse shortcut: the
// rows already link to the same place, so it is skipped by keyboard and screen readers.
export function HistoryPreview({ scan }: { scan: Scan | null }) {
  return (
    <div
      aria-hidden="true"
      className={`sticky top-6 transition-opacity duration-300 ${scan ? "opacity-100" : "pointer-events-none opacity-0"}`}
    >
      <div className="relative aspect-[4/3] overflow-hidden rounded-xl border border-border-strong bg-bg-surface">
        <AnimatePresence initial={false}>
          {scan && (
            <motion.div
              key={scan.id}
              className="absolute inset-0"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              transition={{ duration: DISSOLVE_SECONDS, ease: "easeInOut" }}
            >
              <Link href={`/results/${scan.id}`} tabIndex={-1} className="group block size-full">
                {scan.screenshotUrl ? (
                  // eslint-disable-next-line @next/next/no-img-element -- remote Cloudinary image, no Next image config needed
                  <img src={scan.screenshotUrl} alt="" className="size-full object-cover object-top" />
                ) : (
                  <span className="grid size-full place-items-center bg-violet-700 font-mono text-[40px] font-semibold text-ink">
                    {initials(scan.host)}
                  </span>
                )}
                <div className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/90 via-black/60 to-transparent px-5 pb-4 pt-16">
                  <p className="ts-mono-ui truncate text-[15px] font-semibold text-white group-hover:underline">{scan.host}</p>
                  <p className="truncate font-mono text-[11px] leading-4 text-white/60">{scan.url}</p>
                  <p className="mt-1.5 font-mono text-[11px] leading-4 text-white/80">
                    {[
                      `Scanned ${formatDate(scan.createdAt)}`,
                      formatSeconds(scan.durationMs),
                      `${scan.pages} ${scan.pages === 1 ? "page" : "pages"}`,
                      `${scan.confidence}% confidence`,
                    ].join(" · ")}
                  </p>
                  {scan.tech.length > 0 && (
                    <div className="mt-2 flex flex-wrap gap-1.5">
                      {scan.tech.slice(0, 6).map((t) => (
                        <TechTag key={t.name} name={t.name} />
                      ))}
                    </div>
                  )}
                </div>
              </Link>
            </motion.div>
          )}
        </AnimatePresence>
      </div>
    </div>
  );
}
