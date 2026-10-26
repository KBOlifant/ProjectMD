"use client";

import { useUsage } from "@/app/functions/data/usage";
import { useDenyReason } from "@/app/functions/rbac/useCan";

// Vertical gauge at the foot of the rail: scans used on top, a thin track that fills upward, the
// limit underneath. The numbers come from useUsage() (saved scans in static mode, this month's usage
// from the backend in api mode); stores nothing. While the backend numbers are loading it shows a
// dash instead of a misleading 0. Hidden when the account's role cannot create scans at all
// (read-only viewers), because a scan quota means nothing to them.
export function UsageMeter() {
  const { used, limit, loading } = useUsage();
  const readOnly = useDenyReason("scan:create") === "role";
  if (readOnly) return null;
  const pct = Math.min(100, (used / limit) * 100);
  const text = loading ? "Loading usage" : `${used} of ${limit} scans used`;
  return (
    <div
      title={text}
      className="flex w-11 flex-col items-center gap-1.5 rounded-lg border border-border-subtle bg-bg-surface py-2.5"
    >
      <span aria-hidden="true" className="font-mono text-[10px] leading-none text-ink">
        {loading ? "–" : used}
      </span>
      <div
        role="progressbar"
        aria-label="Scans used"
        aria-valuetext={text}
        aria-busy={loading}
        aria-valuemin={0}
        aria-valuemax={limit}
        aria-valuenow={Math.min(used, limit)}
        className="relative h-9 w-[3px] rounded-[3px] bg-border-strong"
      >
        <div
          className={`absolute inset-x-0 bottom-0 rounded-[3px] ${used >= limit ? "bg-warning" : "bg-violet-500"}`}
          style={{ height: `${pct}%` }}
        />
      </div>
      <span aria-hidden="true" className="font-mono text-[10px] leading-none text-ink-muted">
        {limit}
      </span>
    </div>
  );
}
