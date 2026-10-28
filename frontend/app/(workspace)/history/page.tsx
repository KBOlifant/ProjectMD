"use client";

import { useState } from "react";
import { Can } from "@/app/functions/rbac/can-gate";
import { useCan } from "@/app/functions/rbac/useCan";
import { Eyebrow, PageFrame } from "@/components/shell/page-frame";
import { Button, ButtonLink } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/empty-state";
import { DownloadIcon } from "@/components/ui/icons";
import { HistoryPreview } from "@/components/ui/history-preview";
import { HistoryRow } from "@/components/ui/history-row";
import { downloadAllScans, downloadScan } from "@/lib/download";
import { clearHistory, deleteScan, reloadHistory, useHistory, useHistoryStatus, useIsHydrated } from "@/lib/history-store";

// /history page: lists every saved scan with a ".MD" download on each row, an "Export all" button
// (one combined Markdown file) and a two-step "Clear history" button and a two-step "Delete" button on each row, shown only to accounts with
// the "history:clear" permission. Downloads need the "scan:export" permission. Reads scans from the history store; clearing
// wipes them (localStorage in static mode, the backend in api mode, where a failure keeps the list
// and shows the error). If loading the list from the backend failed it shows the error with a
// Retry button. `confirming`, `clearing` and `clearError` are temporary UI state and are not stored.
export default function HistoryPage() {
  const history = useHistory();
  const hydrated = useIsHydrated();
  const { error: loadError } = useHistoryStatus();
  const canExport = useCan("scan:export");
  const canClear = useCan("history:clear");
  const [confirming, setConfirming] = useState(false);
  const [clearing, setClearing] = useState(false);
  const [clearError, setClearError] = useState<string | null>(null);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [deletingId, setDeletingId] = useState<string | null>(null);

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

  // Deletes one scan (the row already asked for confirmation). A failure (api mode) keeps the list
  // and shows the message under the header.
  async function removeScan(id: string) {
    setDeletingId(id);
    setClearError(null);
    try {
      await deleteScan(id);
    } catch (err) {
      setClearError(err instanceof Error ? err.message : "Could not delete that scan. Try again.");
    }
    setDeletingId(null);
  }

  return (
    <PageFrame size="wide">
      <div className="mb-8 flex flex-wrap items-end justify-between gap-4">
        <div>
          <Eyebrow>Archive</Eyebrow>
          <h1 className="ts-h1 mt-2 text-ink">Scan history</h1>
          {hydrated && !(loadError && history.length === 0) && (
            <p className="ts-body mt-1 text-ink-muted">
              {history.length} completed {history.length === 1 ? "scan" : "scans"}
            </p>
          )}
        </div>
        {history.length > 0 && (
          <div className="flex flex-wrap gap-2">
            <Button
              variant="secondary"
              onClick={() => downloadAllScans(history)}
              disabled={!canExport}
              title={canExport ? undefined : "Your account cannot export scans."}
            >
              <DownloadIcon />
              Export all
            </Button>
            <Can permission="history:clear">
              {confirming ? (
                <>
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
                </>
              ) : (
                <Button variant="ghost" onClick={() => setConfirming(true)}>
                  Clear history
                </Button>
              )}
            </Can>
          </div>
        )}
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
        <EmptyState action={<ButtonLink href="/">Analyze a website</ButtonLink>}>
          No scans yet. Analyze your first website to see it here.
        </EmptyState>
      ) : (
        <div
          onMouseLeave={() => setActiveId(null)}
          onBlur={(e) => {
            if (!e.currentTarget.contains(e.relatedTarget)) setActiveId(null);
          }}
          className="grid items-start gap-8 xl:grid-cols-[minmax(0,960px)_minmax(0,1fr)]"
        >
          <div className="max-w-[960px] space-y-3">
            {history.map((scan) => (
              <HistoryRow
                key={scan.id}
                scan={scan}
                onDownload={() => downloadScan(scan)}
                downloadDisabled={!canExport}
                downloadHint="Your account cannot export scans."
                onDelete={canClear ? () => void removeScan(scan.id) : undefined}
                deleting={deletingId === scan.id}
                onActivate={() => setActiveId(scan.id)}
              />
            ))}
          </div>
          <div className="hidden self-stretch xl:block">
            <HistoryPreview scan={history.find((s) => s.id === activeId) ?? null} />
          </div>
        </div>
      )}
    </PageFrame>
  );
}
