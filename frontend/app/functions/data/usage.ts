"use client";

import { useSyncExternalStore } from "react";
import { useHistory } from "@/lib/history-store";
import { isApiMode } from "./mode";
import { getServerUsageSnapshot, getUsageSnapshot, subscribeUsage, type Usage } from "./usage-store";

export { refreshUsage } from "./usage-store";

// React hook for the sidebar usage meter: scans used this month, plus `loading` (true until the
// first answer from the backend). Static mode: the number of saved scans, never loading. Api mode:
// this month's count from GET /api/me (see usage-store.ts), refreshed by refreshUsage() after a
// scan finishes or history is cleared. There is no cap any more. Stores nothing.
export function useUsage(): Usage {
  const saved = useHistory().length;
  const remote = useSyncExternalStore(subscribeUsage, getUsageSnapshot, getServerUsageSnapshot);
  if (!isApiMode) return { used: saved, loading: false };
  return remote;
}
