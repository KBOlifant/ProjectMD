import Link from "next/link";
import { formatDate, formatSeconds, initials } from "@/lib/format";
import type { Scan } from "@/lib/types";
import { ConfidenceBadge, TechChip } from "./tech-chip";

// One clickable row summarising a saved scan (host, date, duration, pages, first 3 tech chips,
// confidence). Links to /results/<scan.id>. Display only; stores nothing.
export function HistoryRow({ scan }: { scan: Scan }) {
  return (
    <Link
      href={`/results/${scan.id}`}
      className="flex flex-wrap items-center gap-x-4 gap-y-2 rounded-lg border border-border-subtle bg-bg-surface px-4 py-3 transition-colors hover:bg-bg-raised"
    >
      <span className="grid size-[38px] flex-none place-items-center rounded-md bg-violet-700 font-mono text-[11px] font-medium text-ink">
        {initials(scan.host)}
      </span>
      <span className="min-w-0 flex-1 basis-48">
        <span className="ts-mono-ui block truncate text-ink">{scan.host}</span>
        <span className="ts-small block truncate text-ink-muted">
          Scanned {formatDate(scan.createdAt)} · {formatSeconds(scan.durationMs)} · {scan.pages} pages
        </span>
      </span>
      <span className="flex flex-wrap items-center gap-2">
        {scan.tech.slice(0, 3).map((t) => (
          <TechChip key={t.name} name={t.name} />
        ))}
        <ConfidenceBadge value={scan.confidence} />
      </span>
      <span aria-hidden="true" className="ts-mono-ui text-ink-muted">
        →
      </span>
    </Link>
  );
}
