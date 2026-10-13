"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { getIdToken } from "@/app/functions/auth/auth-service";
import { useAuth } from "@/app/functions/auth/useAuth";
import { scanProvider } from "@/app/functions/data/scan-provider";
import { assertCan } from "@/app/functions/rbac/errors";
import { addScan, getHistoryOwner } from "@/lib/history-store";
import { TOTAL_MS } from "@/lib/scan";

// Naming note: THIS file is the React context for the in-progress scan (timer, progress, result
// id). `app/functions/data/scan-provider.ts` is the data source (static mock now, backend API
// later) that this context calls to get the finished Scan.

interface Session {
  url: string;
  elapsed: number;
  scanId: string | null;
  // Set when generating the scan failed; the timer is stopped and no scan is saved.
  error?: string;
}

interface ScanContextValue {
  session: Session | null;
  // Throws PermissionError unless the signed-in user may "scan:create" (callers must catch it).
  startScan: (url: string) => void;
  cancelScan: () => void;
}

const Ctx = createContext<ScanContextValue | null>(null);

// Holds the in-progress scan ("session": url, elapsed ms, finished scan id, optional error) and
// shares it with all pages. While a scan runs, a timer updates `elapsed` every 100ms (the
// progress animation). When TOTAL_MS is reached it asks `scanProvider.generate()` for the Scan,
// SAVES it to the signed-in user's history (localStorage via addScan) and records its id; if
// generating fails it records an error instead. The session itself is only in memory and is
// lost on refresh, and it is reset whenever the signed-in user changes.
export function ScanProvider({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  const uid = user?.uid ?? null;
  const [session, setSession] = useState<Session | null>(null);
  // The uid the current session belongs to; when it changes the session is dropped (see below).
  const [sessionUid, setSessionUid] = useState<string | null>(uid);
  // The running timer, and a counter that is bumped to cancel a run so late results are ignored.
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const runRef = useRef(0);

  // A different account (or sign-out) must never inherit the previous account's scan session.
  if (sessionUid !== uid) {
    setSessionUid(uid);
    setSession(null);
  }

  // Stops the current run: clears the timer and invalidates any scan still being generated, so
  // its result is thrown away. Changes no state.
  const stopRun = useCallback(() => {
    runRef.current += 1;
    if (timerRef.current !== null) {
      clearInterval(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  // Stop any running scan when the signed-in user changes or this provider unmounts.
  useEffect(() => stopRun, [uid, stopRun]);

  // startScan: checks the user may create scans (throws PermissionError if not), then begins a new
  // scan for a URL: resets the timer, no scan id yet. The session is held in memory only; the
  // finished scan is saved to history, but only if the same user is still signed in.
  const startScan = useCallback(
    (next: string) => {
      assertCan(user, "scan:create");
      stopRun();
      const run = runRef.current;
      const owner = getHistoryOwner();
      const t0 = performance.now();
      setSession({ url: next, elapsed: 0, scanId: null });

      const timer = setInterval(() => {
        if (runRef.current !== run) return;
        const elapsed = performance.now() - t0;
        if (elapsed < TOTAL_MS) {
          setSession((s) => (s ? { ...s, elapsed } : s));
          return;
        }

        // Progress animation is done: stop the timer and wait for the provider. Elapsed is held
        // just under TOTAL_MS so the last step stays "current" while the result is generated.
        clearInterval(timer);
        timerRef.current = null;
        setSession((s) => (s ? { ...s, elapsed: TOTAL_MS - 1 } : s));
        scanProvider.generate(next, { getToken: getIdToken }).then(
          (scan) => {
            // Ignore the result if the scan was cancelled/replaced or the user changed meanwhile.
            if (runRef.current !== run || getHistoryOwner() !== owner) return;
            addScan(scan);
            setSession({ url: next, elapsed: TOTAL_MS, scanId: scan.id });
          },
          (err: unknown) => {
            if (runRef.current !== run) return;
            // The provider is responsible for a user-safe message; never log tokens or raw errors here.
            const error = err instanceof Error && err.message ? err.message : "Could not generate documentation. Try again.";
            setSession({ url: next, elapsed: TOTAL_MS - 1, scanId: null, error });
          },
        );
      }, 100);
      timerRef.current = timer;
    },
    [user, stopRun],
  );

  // cancelScan: discards the current session and stops its timer. Does not write to history.
  const cancelScan = useCallback(() => {
    stopRun();
    setSession(null);
  }, [stopRun]);

  const value = useMemo(() => ({ session, startScan, cancelScan }), [session, startScan, cancelScan]);

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

// Hook for pages to read the current scan session and call startScan/cancelScan. Throws if used
// outside ScanProvider. Stores nothing.
export function useScan(): ScanContextValue {
  const ctx = useContext(Ctx);
  if (!ctx) throw new Error("useScan must be used inside ScanProvider");
  return ctx;
}
