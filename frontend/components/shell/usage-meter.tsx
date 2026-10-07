"use client";

import { USAGE_LIMIT, useHistory } from "@/lib/history-store";

// Sidebar bar showing how many scans are saved vs USAGE_LIMIT. Reads the history count;
// stores nothing.
export function UsageMeter() {
  const used = useHistory().length;
  const pct = Math.min(100, (used / USAGE_LIMIT) * 100);
  return (
    <div className="rounded-lg border border-border-subtle bg-bg-surface p-3">
      <div className="ts-label flex justify-between text-ink-dim">
        <span>Usage</span>
        <span>
          {used} / {USAGE_LIMIT} scans
        </span>
      </div>
      <div
        role="progressbar"
        aria-label="Scans used"
        aria-valuemin={0}
        aria-valuemax={USAGE_LIMIT}
        aria-valuenow={Math.min(used, USAGE_LIMIT)}
        className="mt-2 h-[3px] rounded-[3px] bg-bg-raised"
      >
        <div
          className={`h-full rounded-[3px] ${used >= USAGE_LIMIT ? "bg-warning" : "bg-violet-500"}`}
          style={{ width: `${pct}%` }}
        />
      </div>
    </div>
  );
}
