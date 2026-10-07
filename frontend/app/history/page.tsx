"use client";

import { useState } from "react";
import { Button, ButtonLink } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/empty-state";
import { HistoryRow } from "@/components/ui/history-row";
import { clearHistory, useHistory, useIsHydrated } from "@/lib/history-store";

// /history page: lists every saved scan and offers a two-step "Clear history" button.
// Reads scans from the history store; clearing wipes them from localStorage.
// `confirming` is temporary UI state and is not stored.
export default function HistoryPage() {
  const history = useHistory();
  const hydrated = useIsHydrated();
  const [confirming, setConfirming] = useState(false);

  return (
    <div>
      <div className="mb-6 flex items-end justify-between gap-4">
        <div>
          <h1 className="ts-h1 text-ink">Scan History</h1>
          {hydrated && (
            <p className="ts-small mt-1 text-ink-muted">
              {history.length} {history.length === 1 ? "scan" : "scans"} saved in this browser
            </p>
          )}
        </div>
        {history.length > 0 &&
          (confirming ? (
            <div className="flex gap-2">
              <Button variant="ghost" onClick={() => setConfirming(false)}>
                Keep
              </Button>
              <Button
                variant="secondary"
                className="text-danger hover:text-danger"
                onClick={() => {
                  clearHistory();
                  setConfirming(false);
                }}
              >
                Confirm clear
              </Button>
            </div>
          ) : (
            <Button variant="ghost" onClick={() => setConfirming(true)}>
              Clear history
            </Button>
          ))}
      </div>

      {!hydrated ? null : history.length === 0 ? (
        <EmptyState action={<ButtonLink href="/">Generate .MD</ButtonLink>}>
          No scans yet. Generate your first .md file to see it here.
        </EmptyState>
      ) : (
        <div className="space-y-3">
          {history.map((scan) => (
            <HistoryRow key={scan.id} scan={scan} />
          ))}
        </div>
      )}
    </div>
  );
}
