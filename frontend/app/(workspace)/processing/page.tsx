"use client";

import { useRouter } from "next/navigation";
import { useScan } from "@/components/scan-provider";
import { ButtonLink } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/empty-state";
import { ProgressCard, type ProgressStep } from "@/components/ui/progress-card";
import { formatSeconds } from "@/lib/format";
import { PIPELINE_STEPS, stepStates, TOTAL_MS } from "@/lib/scan";

// /processing page: shows the live progress card for the scan in progress (read from ScanProvider).
// Maps the elapsed time to each pipeline step's state. Cancel clears the session and returns home.
// Stores nothing itself; the finished scan is saved by ScanProvider.
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
