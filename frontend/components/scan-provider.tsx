"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import { addScan } from "@/lib/history-store";
import { buildScan, TOTAL_MS } from "@/lib/scan";

interface Session {
  url: string;
  elapsed: number;
  scanId: string | null;
}

interface ScanContext {
  session: Session | null;
  startScan: (url: string) => void;
  cancelScan: () => void;
}

const Ctx = createContext<ScanContext | null>(null);

// Holds the in-progress scan ("session": url, elapsed ms, finished scan id) and shares it with all pages.
// While a scan runs, a timer updates `elapsed` every 100ms; when TOTAL_MS is reached it builds the
// Scan, SAVES it to history (localStorage via addScan) and records its id. The session itself is
// only in memory and is lost on refresh.
export function ScanProvider({ children }: { children: React.ReactNode }) {
  const [session, setSession] = useState<Session | null>(null);
  const url = session?.url;
  const finished = session?.scanId != null;

  useEffect(() => {
    if (!url || finished) return;
    const t0 = performance.now();
    const timer = setInterval(() => {
      const elapsed = performance.now() - t0;
      if (elapsed >= TOTAL_MS) {
        clearInterval(timer);
        const scan = buildScan(url);
        addScan(scan);
        setSession({ url, elapsed: TOTAL_MS, scanId: scan.id });
      } else {
        setSession((s) => (s ? { ...s, elapsed } : s));
      }
    }, 100);
    return () => clearInterval(timer);
  }, [url, finished]);

  // startScan: begins a new scan for a URL (resets the timer, no scan id yet). Held in memory only.
  // cancelScan: discards the current session. Neither writes to history.
  const startScan = useCallback((next: string) => setSession({ url: next, elapsed: 0, scanId: null }), []);
  const cancelScan = useCallback(() => setSession(null), []);
  const value = useMemo(() => ({ session, startScan, cancelScan }), [session, startScan, cancelScan]);

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

// Hook for pages to read the current scan session and call startScan/cancelScan. Throws if used
// outside ScanProvider. Stores nothing.
export function useScan(): ScanContext {
  const ctx = useContext(Ctx);
  if (!ctx) throw new Error("useScan must be used inside ScanProvider");
  return ctx;
}
