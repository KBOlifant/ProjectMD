"use client";

import { useUsage } from "@/app/functions/data/usage";
import { useDenyReason } from "@/app/functions/rbac/useCan";

// Sidebar bar showing how many scans are used vs the limit. The numbers come from useUsage()
// (saved scans in static mode, this month's usage from the backend in api mode); stores nothing.
// While the backend numbers are loading it shows a dash instead of a misleading 0. Hidden when the
// account's role cannot create scans at all (read-only viewers), because a scan quota means
// nothing to them.
export function UsageMeter() {
  const { used, limit, loading } = useUsage();
  const readOnly = useDenyReason("scan:create") === "role";
  if (readOnly) return null;
  const pct = Math.min(100, (used / limit) * 100);
  return (
    <div className="rounded-lg border border-border-subtle bg-bg-surface p-3">
      <div className="ts-label flex justify-between text-ink-dim">
        <span>Usage</span>
        <span>
          {loading ? "–" : used} / {limit} scans
        </span>
      </div>
      <div
        role="progressbar"
        aria-label="Scans used"
        aria-busy={loading}
        aria-valuemin={0}
        aria-valuemax={limit}
        aria-valuenow={Math.min(used, limit)}
        className="mt-2 h-[3px] rounded-[3px] bg-bg-raised"
      >
        <div
          className={`h-full rounded-[3px] ${used >= limit ? "bg-warning" : "bg-violet-500"}`}
          style={{ width: `${pct}%` }}
        />
      </div>
    </div>
  );
}
