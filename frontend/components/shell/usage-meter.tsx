"use client";

import { useUsage } from "@/app/functions/data/usage";
import { useDenyReason } from "@/app/functions/rbac/useCan";
import { ScanIcon } from "@/components/ui/icons";

// Tile at the foot of the rail: how many scans the signed-in user has made this month. There is no
// cap any more, so this is a plain count, not a gauge. The number comes from useUsage() (saved
// scans in static mode, this month's count from the backend in api mode); stores nothing. While the
// backend number is loading it shows a dash instead of a misleading 0. Hidden when the account's
// role cannot create scans at all (read-only viewers), because a scan count means nothing to them.
export function UsageMeter() {
  const { used, loading } = useUsage();
  const readOnly = useDenyReason("scan:create") === "role";
  if (readOnly) return null;
  const text = loading ? "Loading usage" : `${used} ${used === 1 ? "scan" : "scans"} this month`;
  return (
    <div title={text} className="flex w-11 flex-col items-center gap-1.5 rounded-lg border border-border-subtle bg-bg-surface py-2.5">
      <ScanIcon size={14} aria-hidden="true" className="text-ink-dim" />
      <span aria-live="polite" aria-label={text} className="font-mono text-[11px] leading-none text-ink">
        {loading ? "–" : used}
      </span>
    </div>
  );
}
