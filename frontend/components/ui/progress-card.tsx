import { CheckIcon } from "./icons";
import { Button, ButtonLink } from "./button";
import { initials } from "@/lib/format";

export interface ProgressStep {
  title: string;
  detail: string;
  state: "done" | "current" | "queued";
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

// Card showing scan progress: host/url header, percentage bar, the list of pipeline steps with
// their state, a Cancel button and a "View Results" button that enables when complete.
// Purely presentational; all data comes in through props and nothing is stored.
export function ProgressCard({ host, url, steps, progress, complete, onCancel, resultsHref }: ProgressCardProps) {
  const pct = Math.round(progress);
  return (
    <div className="w-full max-w-[480px] overflow-hidden rounded-xl border border-border-subtle bg-bg-surface shadow-panel">
      <div className="flex items-center gap-3 border-b border-border-subtle px-6 py-4">
        <span className="grid size-[38px] flex-none place-items-center rounded-md bg-fig-500 ts-micro font-medium text-ink">
          {initials(host)}
        </span>
        <div className="min-w-0">
          <p className="ts-mono-ui truncate text-ink">{host}</p>
          <p className="truncate ts-micro text-ink-muted">{url}</p>
        </div>
        <span
          className={`ts-label ml-auto rounded-pill border px-2.5 py-1 ${
            complete ? "border-border-strong text-ink-muted" : "border-fig-500 text-rose-400"
          }`}
        >
          {complete ? "Complete" : "Scanning"}
        </span>
      </div>

      <div className="px-6 pb-6 pt-4">
        <div className="ts-label flex justify-between text-ink-dim">
          <span>Progress</span>
          <span>{pct}%</span>
        </div>
        <div
          role="progressbar"
          aria-label="Scan progress"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={pct}
          className="mb-4 mt-2 h-[3px] rounded-xs bg-bg-raised"
        >
          <div className="h-full rounded-xs bg-mulberry-500" style={{ width: `${pct}%` }} />
        </div>

        <ol>
          {steps.map((s) => (
            <li key={s.title} className="flex gap-3 border-t border-border-subtle py-3">
              <span
                aria-hidden="true"
                className={`mt-0.5 grid size-[18px] flex-none place-items-center rounded-full border-[1.5px] ${
                  s.state === "queued"
                    ? "border-border-strong"
                    : s.state === "current"
                      ? "border-mulberry-500 bg-fig-500"
                      : "border-mulberry-500 text-rose-400"
                }`}
              >
                {s.state === "done" && <CheckIcon width={10} height={10} />}
              </span>
              <div className="min-w-0 flex-1">
                <p
                  className={`ts-mono-ui ${
                    s.state === "current" ? "text-ink" : s.state === "done" ? "text-ink-muted" : "text-ink-dim"
                  }`}
                >
                  {s.title}
                </p>
                <p
                  aria-live={s.state === "current" ? "polite" : undefined}
                  className={`ts-small ${s.state === "current" ? "text-ink-muted" : "text-ink-dim"}`}
                >
                  {s.detail}
                </p>
              </div>
            </li>
          ))}
        </ol>

        <div className="mt-2 flex gap-3">
          <Button variant="ghost" onClick={onCancel}>
            Cancel
          </Button>
          {resultsHref ? (
            <ButtonLink href={resultsHref} className="flex-1">
              View Results →
            </ButtonLink>
          ) : (
            <Button disabled className="flex-1">
              View Results →
            </Button>
          )}
        </div>
      </div>
    </div>
  );
}
