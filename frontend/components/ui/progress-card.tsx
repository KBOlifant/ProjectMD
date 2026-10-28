"use client";

import { motion } from "framer-motion";
import type { ReactNode } from "react";
import { Button, ButtonLink } from "./button";
import { ArrowRightIcon } from "./icons";
import { initials } from "@/lib/format";

type CardStatus = "scanning" | "complete" | "failed";

const STATUS_LABEL: Record<CardStatus, string> = { scanning: "Scanning", complete: "Complete", failed: "Failed" };
const STATUS_TONE: Record<CardStatus, string> = {
  scanning: "border-plum-500 text-plum-300",
  complete: "border-border-strong text-ink-muted",
  failed: "border-danger text-danger",
};

// Frame shared by the progress card and the failed card: rounded panel, 720px max. While
// `glowing`, the `glow-border` class (globals.css) draws a gold-to-purple border on ::after whose
// gradient spins, plus a blurred halo, to show the real generation call is in flight.
export function ScanCardFrame({ children, glowing = false }: { children: ReactNode; glowing?: boolean }) {
  return (
    <div className={`relative w-full max-w-[720px] rounded-xl ${glowing ? "glow-border" : ""}`}>
      <div className="relative overflow-hidden rounded-xl border border-border-strong bg-bg-surface bg-trneumorphic-panel">{children}</div>
    </div>
  );
}

// Header row of the scan card: initials tile, host, URL and a status pill. Stores nothing.
export function ScanCardHeader({ host, url, status }: { host: string; url: string; status: CardStatus }) {
  return (
    <div className="flex items-center gap-4 border-b border-border-subtle px-7 py-5">
      <span className="grid size-12 flex-none place-items-center rounded-md border border-violet-500/40 bg-violet-700 font-mono text-[14px] font-semibold text-ink">
        {initials(host)}
      </span>
      <div className="min-w-0">
        <p className="ts-mono-ui truncate text-[16px] font-semibold text-ink">{host}</p>
        <p className="truncate font-mono text-[12px] leading-4 text-ink-muted">{url}</p>
      </div>
      <span className={`ts-label ml-auto inline-flex flex-none items-center gap-1.5 rounded-pill border px-2.5 py-1 ${STATUS_TONE[status]}`}>
        {status === "scanning" && <span aria-hidden="true" className="size-1.5 rounded-full bg-plum-400" />}
        {STATUS_LABEL[status]}
      </span>
    </div>
  );
}

// The status line under the bar: changes wording as the fake ramp approaches its ceiling, since
// there are no real pipeline steps to report (the AI call's own timing is unknown until it answers).
function progressLabel(pct: number, complete: boolean): string {
  if (complete) return "Documentation ready";
  if (pct >= 80) return "Almost there, finishing up…";
  if (pct >= 35) return "Writing the documentation…";
  return "Analyzing the site…";
}

interface ProgressCardProps {
  host: string;
  url: string;
  progress: number;
  complete: boolean;
  onCancel: () => void;
  resultsHref: string | null;
}

// Card showing scan progress: host/url header, one thick percentage bar (no per-step breakdown,
// since the real call's timing can't be predicted), a Cancel button and a "View results" button
// that unlocks when complete. Purely presentational; all data comes in through props and nothing
// is stored.
export function ProgressCard({ host, url, progress, complete, onCancel, resultsHref }: ProgressCardProps) {
  const pct = Math.round(progress);
  return (
    <ScanCardFrame glowing={!complete}>
      <ScanCardHeader host={host} url={url} status={complete ? "complete" : "scanning"} />

      <div className="px-7 pb-7 pt-6">
        <div className="ts-label flex justify-between text-ink-muted">
          <span>Analysis progress</span>
          <span className="text-ink">{pct}%</span>
        </div>
        <div
          role="progressbar"
          aria-label="Scan progress"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={pct}
          className="mt-3 h-4 overflow-hidden rounded-pill bg-border-strong"
        >
          <motion.div
            className="h-full rounded-pill bg-gradient-to-r from-plum-400 to-violet-500"
            animate={{ width: `${pct}%` }}
            transition={{ ease: "easeOut", duration: 0.3 }}
          />
        </div>
        <p aria-live="polite" className="ts-body mt-4 text-ink-muted">
          {progressLabel(pct, complete)}
        </p>

        <div className="mt-7 flex justify-end gap-3">
          <Button variant="ghost" onClick={onCancel}>
            Cancel
          </Button>
          {resultsHref ? (
            <ButtonLink href={resultsHref}>
              View results
              <ArrowRightIcon />
            </ButtonLink>
          ) : (
            <Button disabled>
              View results
              <ArrowRightIcon />
            </Button>
          )}
        </div>
      </div>
    </ScanCardFrame>
  );
}
