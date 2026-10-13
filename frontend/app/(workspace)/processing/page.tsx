"use client";

import { useRouter } from "next/navigation";
import { useScan } from "@/components/scan-provider";
import { Button, ButtonLink } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/empty-state";
import { ProgressCard, type ProgressStep } from "@/components/ui/progress-card";
import { formatSeconds, initials } from "@/lib/format";
import { PIPELINE_STEPS, stepStates, TOTAL_MS } from "@/lib/scan";

// Card shown instead of the progress card when generating the docs failed. Same frame and header
// as the progress card, with the error message and a "Try again" button (the caller clears the
// session and returns to Generate). Purely presentational; stores nothing.
function FailedCard({ host, url, message, onRetry }: { host: string; url: string; message: string; onRetry: () => void }) {
  return (
    <div className="w-full max-w-[480px] overflow-hidden rounded-xl border border-border-subtle bg-bg-surface shadow-panel">
      <div className="flex items-center gap-3 border-b border-border-subtle px-6 py-4">
        <span className="grid size-[38px] flex-none place-items-center rounded-md bg-violet-700 font-mono text-[11px] font-medium text-ink">
          {initials(host)}
        </span>
        <div className="min-w-0">
          <p className="ts-mono-ui truncate text-ink">{host}</p>
          <p className="truncate font-mono text-[11px] leading-4 text-ink-muted">{url}</p>
        </div>
        <span className="ts-label ml-auto rounded-pill border border-danger px-2.5 py-1 text-danger">Failed</span>
      </div>
      <div className="px-6 pb-6 pt-4">
        <p role="alert" className="ts-body text-danger">
          {message}
        </p>
        <div className="mt-4">
          <Button onClick={onRetry}>Try again</Button>
        </div>
      </div>
    </div>
  );
}

// /processing page: shows the live progress card for the scan in progress (read from ScanProvider).
// Maps the elapsed time to each pipeline step's state. Cancel clears the session and returns home.
// If generating the docs failed (`session.error`) it shows the error with "Try again", which
// clears the session and returns to Generate. Stores nothing itself; the finished scan is saved by
// ScanProvider.
export default function ProcessingPage() {
  const router = useRouter();
  const { session, cancelScan } = useScan();

  if (!session) {
    return (
      <div className="pt-8">
        <EmptyState action={<ButtonLink href="/">Go to Generate</ButtonLink>}>
          Nothing is scanning. Paste a URL on Generate to start.
        </EmptyState>
      </div>
    );
  }

  const complete = session.scanId !== null;
  const states = stepStates(session.elapsed);
  const steps: ProgressStep[] = PIPELINE_STEPS.map((s, i) => ({
    title: s.title,
    detail: states[i] === "done" ? s.done : states[i] === "current" ? s.active : "Queued",
    state: states[i],
    time: states[i] === "done" ? formatSeconds(s.ms) : undefined,
  }));
  const host = new URL(session.url).hostname.replace(/^www\./, "");

  if (session.error) {
    return (
      <div className="flex justify-center pt-8">
        <FailedCard
          host={host}
          url={session.url}
          message={session.error}
          onRetry={() => {
            cancelScan();
            router.push("/");
          }}
        />
      </div>
    );
  }

  return (
    <div className="flex justify-center pt-8">
      <ProgressCard
        host={host}
        url={session.url}
        steps={steps}
        progress={(session.elapsed / TOTAL_MS) * 100}
        complete={complete}
        resultsHref={complete ? `/results/${session.scanId}` : null}
        onCancel={() => {
          cancelScan();
          router.push("/");
        }}
      />
    </div>
  );
}
