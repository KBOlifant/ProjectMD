"use client";

import { useSyncExternalStore } from "react";
import { useHistory } from "@/lib/history-store";
import { isApiMode } from "./mode";
import { getServerUsageSnapshot, getUsageSnapshot, subscribeUsage, USAGE_LIMIT, type Usage } from "./usage-store";

export { refreshUsage } from "./usage-store";

// React hook for the sidebar usage meter: `used` scans out of `limit`, plus `loading` (true until
// the first answer from the backend). Static mode: the number of saved scans against USAGE_LIMIT,
// never loading. Api mode: this month's numbers from GET /api/me (see usage-store.ts), refreshed by
// refreshUsage() after a scan finishes or history is cleared. Stores nothing.
export function useUsage(): Usage {
  const saved = useHistory().length;
  const remote = useSyncExternalStore(subscribeUsage, getUsageSnapshot, getServerUsageSnapshot);
  if (!isApiMode) return { used: saved, limit: USAGE_LIMIT, loading: false };
  return remote;
}
