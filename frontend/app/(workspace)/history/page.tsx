"use client";

import { useState } from "react";
import { isApiMode } from "@/app/functions/data/mode";
import { Can } from "@/app/functions/rbac/can-gate";
import { Button, ButtonLink } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/empty-state";
import { HistoryRow } from "@/components/ui/history-row";
import { clearHistory, reloadHistory, useHistory, useHistoryStatus, useIsHydrated } from "@/lib/history-store";

// /history page: lists every saved scan and offers a two-step "Clear history" button, shown only
// to accounts with the "history:clear" permission. Reads scans from the history store; clearing
// wipes them (localStorage in static mode, the backend in api mode, where a failure keeps the list
// and shows the error). If loading the list from the backend failed it shows the error with a
// Retry button. `confirming`, `clearing` and `clearError` are temporary UI state and are not stored.
export default function HistoryPage() {
  const history = useHistory();
  const hydrated = useIsHydrated();
  const { error: loadError } = useHistoryStatus();
  const [confirming, setConfirming] = useState(false);
  const [clearing, setClearing] = useState(false);
  const [clearError, setClearError] = useState<string | null>(null);

  // Clears the history and closes the confirm step. A failure (api mode) leaves the list as it is
  // and shows the message under the header.
  async function confirmClear() {
    setClearing(true);
    setClearError(null);
    try {
      await clearHistory();
    } catch (err) {
      setClearError(err instanceof Error ? err.message : "Could not clear your history. Try again.");
    }
    setClearing(false);
    setConfirming(false);
  }

  return (
    <div>
      <div className="mb-6 flex items-end justify-between gap-4">
        <div>
          <h1 className="ts-h1 text-ink">Scan History</h1>
          {hydrated && !(loadError && history.length === 0) && (
            <p className="ts-small mt-1 text-ink-muted">
              {history.length} {history.length === 1 ? "scan" : "scans"} saved {isApiMode ? "to your account" : "in this browser"}
            </p>
          )}
        </div>
        <Can permission="history:clear">
          {history.length > 0 &&
            (confirming ? (
              <div className="flex gap-2">
                <Button variant="ghost" onClick={() => setConfirming(false)} disabled={clearing}>
                  Keep
                </Button>
                <Button
                  variant="secondary"
                  className="text-danger hover:text-danger"
                  onClick={confirmClear}
                  disabled={clearing}
                >
                  Confirm clear
                </Button>
              </div>
            ) : (
              <Button variant="ghost" onClick={() => setConfirming(true)}>
                Clear history
              </Button>
            ))}
        </Can>
      </div>

      {(clearError ?? (history.length > 0 ? loadError : null)) && (
        <p role="alert" className="ts-small mb-4 text-danger">
          {clearError ?? loadError}
        </p>
      )}

      {!hydrated ? null : loadError && history.length === 0 ? (
        <EmptyState action={<Button onClick={reloadHistory}>Retry</Button>}>
          <span role="alert" className="text-danger">
            {loadError}
          </span>
        </EmptyState>
      ) : history.length === 0 ? (
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
