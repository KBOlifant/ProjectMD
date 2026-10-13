"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { PermissionError } from "@/app/functions/rbac/errors";
import { useCan, useDenyReason } from "@/app/functions/rbac/useCan";
import { ScanBlockedNotice } from "@/components/auth-status/scan-blocked-notice";
import { useScan } from "@/components/scan-provider";
import { EmptyState } from "@/components/ui/empty-state";
import { HistoryRow } from "@/components/ui/history-row";
import { StatStrip } from "@/components/ui/stat-tile";
import { UrlInput } from "@/components/ui/url-input";
import { formatSeconds } from "@/lib/format";
import { useHistory, useIsHydrated } from "@/lib/history-store";
import { parseUrl } from "@/lib/url";

// Home page (/): URL box, summary stats and the 3 latest scans. Stats are calculated from the saved
// history on every render (not stored). The typed URL and error are temporary state, not stored.
// The URL box is disabled (with the reason shown) when the account may not create scans.
export default function GeneratePage() {
  const router = useRouter();
  const { startScan } = useScan();
  const history = useHistory();
  const hydrated = useIsHydrated();
  const canCreate = useCan("scan:create");
  const denyReason = useDenyReason("scan:create");
  const [value, setValue] = useState("");
  const [error, setError] = useState<string | null>(null);

  // Called when the form is submitted: validates the typed URL with parseUrl. On error it shows
  // the message; otherwise it starts a scan and goes to /processing. If startScan refuses with a
  // PermissionError (signed out, unverified email or read-only role) its message is shown as the
  // form error and we stay here. Nothing is saved yet.
  function submit() {
    const parsed = parseUrl(value);
    if (!parsed.ok) return setError(parsed.error);
    setError(null);
    try {
      startScan(parsed.url);
    } catch (err) {
      if (err instanceof PermissionError) return setError(err.message);
      throw err;
    }
    router.push("/processing");
  }

  const n = history.length;
  const avg = (pick: (s: (typeof history)[number]) => number) =>
    history.reduce((sum, s) => sum + pick(s), 0) / n;
  const stats = [
    { value: String(n), caption: "Total Scans" },
    { value: n ? `${Math.round(avg((s) => s.confidence))}%` : "—", caption: "Avg Confidence" },
    { value: String(history.reduce((sum, s) => sum + s.pages, 0)), caption: "Pages Indexed" },
    { value: n ? formatSeconds(avg((s) => s.durationMs)) : "—", caption: "Avg Speed" },
  ];

  return (
    <div className="space-y-12">
      <section className="hero-glow -mx-4 -mt-8 px-4 pb-4 pt-12 md:-mx-8 md:px-8">
        <span className="ts-label inline-block rounded-pill border border-violet-700 px-3 py-1 text-violet-400">
          AI-Powered · Live
        </span>
        <h1 className="ts-display mt-6 text-ink">
          Website → Documentation.
          <br />
          <span className="text-violet-400">Intelligently.</span>
        </h1>
        <p className="ts-body mt-4 max-w-xl text-ink-muted">
          Paste any URL. Project MD fetches and renders the page, fingerprints the tech stack, and writes the docs as
          Markdown.
        </p>
        <div className="mt-8 max-w-2xl">
          <UrlInput
            value={value}
            onChange={setValue}
            onSubmit={submit}
            error={error}
            disabled={!canCreate}
            notice={denyReason ? <ScanBlockedNotice reason={denyReason} /> : undefined}
          />
        </div>
      </section>

      <StatStrip stats={stats} />

      <section aria-labelledby="recent">
        <div className="mb-4 flex items-center justify-between">
          <h2 id="recent" className="ts-label text-ink-dim">
            Recent scans
          </h2>
          {n > 3 && (
            <Link href="/history" className="ts-mono-ui text-violet-400 hover:text-ink">
              View all →
            </Link>
          )}
        </div>
        {!hydrated ? null : n === 0 ? (
          <EmptyState>No scans yet. Generate your first .md file above to see it here.</EmptyState>
        ) : (
          <div className="space-y-3">
            {history.slice(0, 3).map((scan) => (
              <HistoryRow key={scan.id} scan={scan} />
            ))}
          </div>
        )}
      </section>
    </div>
  );
}
