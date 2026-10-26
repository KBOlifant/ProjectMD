import type { ReactNode } from "react";
import { Button, ButtonLink } from "./button";
import { ArrowRightIcon, CheckIcon } from "./icons";
import { initials } from "@/lib/format";

export interface ProgressStep {
  title: string;
  detail: string;
  state: "done" | "current" | "queued";
}

type CardStatus = "scanning" | "complete" | "failed";

const STATUS_LABEL: Record<CardStatus, string> = { scanning: "Scanning", complete: "Complete", failed: "Failed" };
const STATUS_TONE: Record<CardStatus, string> = {
  scanning: "border-plum-500 text-plum-300",
  complete: "border-border-strong text-ink-muted",
  failed: "border-danger text-danger",
};

// Frame shared by the progress card and the failed card: rounded panel, 520px max. Stores nothing.
export function ScanCardFrame({ children }: { children: ReactNode }) {
  return <div className="w-full max-w-[520px] overflow-hidden rounded-xl border border-border-strong bg-bg-surface shadow-panel">{children}</div>;
}

// Header row of the scan card: initials tile, host, URL and a status pill. Stores nothing.
export function ScanCardHeader({ host, url, status }: { host: string; url: string; status: CardStatus }) {
  return (
    <div className="flex items-center gap-3 border-b border-border-subtle px-5 py-4">
      <span className="grid size-10 flex-none place-items-center rounded-md border border-violet-500/40 bg-violet-700 font-mono text-[11px] font-semibold text-ink">
        {initials(host)}
      </span>
      <div className="min-w-0">
        <p className="ts-mono-ui truncate font-semibold text-ink">{host}</p>
        <p className="truncate font-mono text-[10px] leading-4 text-ink-muted">{url}</p>
      </div>
      <span className={`ts-label ml-auto inline-flex flex-none items-center gap-1.5 rounded-pill border px-2.5 py-1 ${STATUS_TONE[status]}`}>
        {status === "scanning" && <span aria-hidden="true" className="size-1.5 rounded-full bg-plum-400" />}
        {STATUS_LABEL[status]}
      </span>
    </div>
  );
}

interface ProgressCardProps {
  host: string;
  url: string;
  steps: ProgressStep[];
  progress: number;
  complete: boolean;
  onCancel: () => void;
  resultsHref: string | null;
}

// Card showing scan progress: host/url header, percentage bar, the numbered pipeline steps with
// their state, a Cancel button and a "View results" button that unlocks when complete.
// Purely presentational; all data comes in through props and nothing is stored.
export function ProgressCard({ host, url, steps, progress, complete, onCancel, resultsHref }: ProgressCardProps) {
  const pct = Math.round(progress);
  return (
    <ScanCardFrame>
      <ScanCardHeader host={host} url={url} status={complete ? "complete" : "scanning"} />

      <div className="px-5 pb-5 pt-4">
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
          className="mb-5 mt-2.5 h-[3px] rounded-[3px] bg-border-strong"
        >
          <div className="h-full rounded-[3px] bg-plum-400" style={{ width: `${pct}%` }} />
        </div>

        <ol>
          {steps.map((s, i) => (
            <li key={s.title} className="flex gap-3 border-b border-border-subtle py-3.5">
              <span
                aria-hidden="true"
                className={`grid size-[22px] flex-none place-items-center rounded-full border font-mono text-[9px] ${
                  s.state === "queued"
                    ? "border-border-strong text-ink-dim"
                    : s.state === "current"
                      ? "border-violet-400 text-ink"
                      : "border-violet-500 bg-violet-700 text-ink"
                }`}
              >
                {s.state === "done" ? <CheckIcon size={11} /> : i + 1}
              </span>
              <div className="min-w-0 flex-1">
                <p
                  className={`ts-mono-ui ${
                    s.state === "current" ? "font-semibold text-ink" : s.state === "done" ? "text-ink-body" : "text-ink-dim"
                  }`}
                >
                  {s.title}
                </p>
                <p
                  aria-live={s.state === "current" ? "polite" : undefined}
                  className={`ts-small ${s.state === "current" ? "text-ink-body" : s.state === "done" ? "text-ink-muted" : "text-ink-dim"}`}
                >
                  {s.detail}
                </p>
              </div>
            </li>
          ))}
        </ol>

        <div className="mt-5 flex justify-end gap-3">
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
