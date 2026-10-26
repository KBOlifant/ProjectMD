"use client";

import { useRouter } from "next/navigation";
import { useScan } from "@/components/scan-provider";
import { Button, ButtonLink } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/empty-state";
import { ProgressCard, ScanCardFrame, ScanCardHeader, type ProgressStep } from "@/components/ui/progress-card";
import { formatSeconds } from "@/lib/format";
import { PIPELINE_STEPS, stepStates, TOTAL_MS } from "@/lib/scan";

// Card shown instead of the progress card when generating the docs failed. Same frame and header
// as the progress card (shared from ui/progress-card), with the error message and a "Try again" button (the caller clears the
// session and returns to Generate). Purely presentational; stores nothing.
function FailedCard({ host, url, message, onRetry }: { host: string; url: string; message: string; onRetry: () => void }) {
  return (
    <ScanCardFrame>
      <ScanCardHeader host={host} url={url} status="failed" />
      <div className="px-5 pb-5 pt-4">
        <p role="alert" className="ts-body text-danger">
          {message}
        </p>
        <div className="mt-4 flex justify-end">
          <Button onClick={onRetry}>Try again</Button>
        </div>
      </div>
    </ScanCardFrame>
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
      <div className="flex min-h-screen items-center justify-center px-4 py-10">
        <div className="w-full max-w-[520px]">
          <EmptyState action={<ButtonLink href="/">Go to Generate</ButtonLink>}>
            Nothing is scanning. Paste a URL on Generate to start.
          </EmptyState>
        </div>
      </div>
    );
  }

  const complete = session.scanId !== null;
  const states = stepStates(session.elapsed);
  const steps: ProgressStep[] = PIPELINE_STEPS.map((s, i) => ({
    title: s.title,
    detail: states[i] === "done" ? `${s.done} in ${formatSeconds(s.ms)}` : states[i] === "current" ? s.active : "Queued",
    state: states[i],
  }));
  const host = new URL(session.url).hostname.replace(/^www\./, "");

  if (session.error) {
    return (
      <div className="flex min-h-screen items-center justify-center px-4 py-10">
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
    <div className="flex min-h-screen items-center justify-center px-4 py-10">
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
