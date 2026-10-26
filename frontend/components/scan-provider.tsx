"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { getIdToken } from "@/app/functions/auth/auth-service";
import { useAuth } from "@/app/functions/auth/useAuth";
import { scanProvider } from "@/app/functions/data/scan-provider";
import { refreshUsage } from "@/app/functions/data/usage";
import { assertCan } from "@/app/functions/rbac/errors";
import { addScan, getHistoryOwner } from "@/lib/history-store";
import { easeTowardCeiling } from "@/lib/scan";

// Naming note: THIS file is the React context for the in-progress scan (progress, result id).
// `app/functions/data/scan-provider.ts` is the data source (static mock now, backend API later)
// that this context calls to get the finished Scan.

interface Session {
  url: string;
  // 0-100. Eases toward lib/scan's FAKE_CEILING while the real call is in flight (its actual
  // duration is unknown), then jumps to 100 once the result arrives.
  progress: number;
  scanId: string | null;
  // Set when generating the scan failed; the ramp is stopped and no scan is saved.
  error?: string;
}

interface ScanContextValue {
  session: Session | null;
  // Throws PermissionError unless the signed-in user may "scan:create" (callers must catch it).
  startScan: (url: string) => void;
  cancelScan: () => void;
}

const Ctx = createContext<ScanContextValue | null>(null);

// Holds the in-progress scan ("session": url, fake-ramped progress, finished scan id, optional
// error) and shares it with all pages. startScan calls scanProvider.generate() right away (no
// artificial delay first) and, in parallel, eases `progress` toward FAKE_CEILING every 100ms so
// the bar keeps moving for as long as the real call takes. When the call resolves it SAVES the
// scan to the signed-in user's history (addScan: localStorage in static mode, in-memory copy of
// the backend's list in api mode), refreshes the usage numbers and jumps progress to 100; if it
// fails it records the provider's message as the error instead. The session itself is only in
// memory and is lost on refresh, and it is reset whenever the signed-in user changes.
export function ScanProvider({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  const uid = user?.uid ?? null;
  const [session, setSession] = useState<Session | null>(null);
  // The uid the current session belongs to; when it changes the session is dropped (see below).
  const [sessionUid, setSessionUid] = useState<string | null>(uid);
  // The running ramp timer, and a counter that is bumped to cancel a run so late results are ignored.
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const runRef = useRef(0);

  // A different account (or sign-out) must never inherit the previous account's scan session.
  if (sessionUid !== uid) {
    setSessionUid(uid);
    setSession(null);
  }

  // Stops the current run: clears the ramp timer and invalidates any scan still being generated,
  // so its result is thrown away. Changes no state.
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
  // scan for a URL: starts the real generate() call immediately and a fake progress ramp beside
  // it. The session is held in memory only; the finished scan is saved to history, but only if the
  // same user is still signed in.
  const startScan = useCallback(
    (next: string) => {
      assertCan(user, "scan:create");
      stopRun();
      const run = runRef.current;
      const owner = getHistoryOwner();
      const t0 = performance.now();
      setSession({ url: next, progress: 0, scanId: null });

      const timer = setInterval(() => {
        if (runRef.current !== run) return;
        setSession((s) => (s ? { ...s, progress: easeTowardCeiling(performance.now() - t0) } : s));
      }, 100);
      timerRef.current = timer;

      // Stops just the ramp timer for this run (not stopRun(): that also bumps runRef, which would
      // make the "was this run replaced?" check below always fail).
      const stopRamp = () => {
        if (timerRef.current !== null) {
          clearInterval(timerRef.current);
          timerRef.current = null;
        }
      };

      scanProvider.generate(next, { getToken: getIdToken }).then(
        (scan) => {
          stopRamp();
          // Ignore the result if the scan was cancelled/replaced or the user changed meanwhile.
          if (runRef.current !== run || getHistoryOwner() !== owner) return;
          addScan(scan);
          void refreshUsage();
          setSession({ url: next, progress: 100, scanId: scan.id });
        },
        (err: unknown) => {
          stopRamp();
          if (runRef.current !== run) return;
          // The provider is responsible for a user-safe message; never log tokens or raw errors here.
          const error = err instanceof Error && err.message ? err.message : "Could not generate documentation. Try again.";
          // A refused scan (for example the monthly quota) may mean the usage numbers are out of date.
          void refreshUsage();
          setSession((s) => ({ url: next, progress: s?.progress ?? 0, scanId: null, error }));
        },
      );
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
