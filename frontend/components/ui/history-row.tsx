import Link from "next/link";
import { formatDate, initials } from "@/lib/format";
import type { Scan } from "@/lib/types";
import { DownloadIcon } from "./icons";
import { ConfidenceBadge, TechTag } from "./tech-chip";

interface HistoryRowProps {
  scan: Scan;
  // When given, a ".MD" download button is shown at the right edge of the row.
  onDownload?: () => void;
  // Disables that button (for accounts without export permission); `downloadHint` explains why.
  downloadDisabled?: boolean;
  downloadHint?: string;
}

// One row summarising a saved scan: initials tile, host with confidence badge, up to four tech
// tags, scan date, page count and a ".MD" download button. The host is a real link to
// /results/<scan.id> stretched over the whole row; the download button sits above that link so
// the two never nest. Display only; stores nothing.
export function HistoryRow({ scan, onDownload, downloadDisabled = false, downloadHint }: HistoryRowProps) {
  return (
    <div className="relative flex flex-wrap items-center gap-x-4 gap-y-3 rounded-lg border border-border-subtle bg-bg-surface px-4 py-3.5 transition-colors focus-within:border-border-strong hover:border-border-strong hover:bg-bg-raised">
      <span className="grid size-10 flex-none place-items-center rounded-md border border-violet-500/40 bg-violet-700 font-mono text-[11px] font-semibold text-ink">
        {initials(scan.host)}
      </span>
      <div className="min-w-0 flex-1 basis-56">
        <div className="flex items-center gap-2">
          <Link
            href={`/results/${scan.id}`}
            className="ts-mono-ui truncate font-semibold text-ink after:absolute after:inset-0 after:content-['']"
          >
            {scan.host}
          </Link>
          <ConfidenceBadge value={scan.confidence} />
        </div>
        <div className="mt-1.5 flex flex-wrap gap-1.5">
          {scan.tech.slice(0, 4).map((t) => (
            <TechTag key={t.name} name={t.name} />
          ))}
        </div>
      </div>
      <div className="flex flex-none items-center gap-4">
        <div className="text-right">
          <p className="font-mono text-[11px] leading-4 text-ink-body">{formatDate(scan.createdAt)}</p>
          <p className="font-mono text-[10px] leading-4 text-ink-muted">
            {scan.pages} {scan.pages === 1 ? "page" : "pages"}
          </p>
        </div>
        {onDownload && (
          <button
            type="button"
            onClick={onDownload}
            disabled={downloadDisabled}
            title={downloadDisabled ? downloadHint : `Download ${scan.host}.md`}
            aria-label={`Download ${scan.host} as .md`}
            className="ts-mono-ui relative z-10 inline-flex items-center gap-2 rounded-md border border-border-strong px-3 py-1.5 text-ink-body transition-colors hover:bg-bg-base hover:text-ink disabled:cursor-not-allowed disabled:opacity-50"
          >
            <DownloadIcon size={13} />
            .MD
          </button>
        )}
      </div>
    </div>
  );
}
