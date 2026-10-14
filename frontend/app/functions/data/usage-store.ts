import { getMeSnapshot, loadMe, setMeOwner, subscribeMe } from "./me-store";
import { isApiMode } from "./mode";

// The monthly scan quota the UI shows in static mode, and the placeholder in api mode until the
// backend's real limit (GET /api/me) has loaded.
export const USAGE_LIMIT = 10;

export interface Usage {
  used: number;
  limit: number;
  loading: boolean;
}

// What the server render (and the first client paint) sees: nothing loaded yet.
const SERVER_SNAPSHOT: Usage = { used: 0, limit: USAGE_LIMIT, loading: true };
// The numbers handed out last, and the /api/me state they were worked out from, so the snapshot is
// the same object until the numbers really change (useSyncExternalStore needs that).
let lastSource: ReturnType<typeof getMeSnapshot> | null = null;
let lastUsage: Usage = { used: 0, limit: USAGE_LIMIT, loading: false };

// Registers a callback to run when the usage changes (used by useSyncExternalStore). The numbers
// come from the shared /api/me store, so this simply listens to it. Returns a function that removes it.
export function subscribeUsage(cb: () => void) {
  return subscribeMe(cb);
}

// Returns the current usage numbers (the same object until they change). They are read from the
// shared /api/me answer (see me-store.ts): loading until the first answer for the signed-in user
// arrives; if that answer failed, the placeholder numbers stay and loading ends.
export function getUsageSnapshot(): Usage {
  const source = getMeSnapshot();
  if (source === lastSource) return lastUsage;
  lastSource = source;
  const next: Usage = {
    used: source.me?.usage.used ?? 0,
    limit: source.me?.usage.limit ?? USAGE_LIMIT,
    loading: source.owner !== null && !source.loaded,
  };
  if (next.used !== lastUsage.used || next.limit !== lastUsage.limit || next.loading !== lastUsage.loading) {
    lastUsage = next;
  }
  return lastUsage;
}

// Returns the usage the server render should use.
export function getServerUsageSnapshot(): Usage {
  return SERVER_SNAPSHOT;
}

// Tells the store whose usage to track (the signed-in uid, or null when signed out). In api mode it
// hands the uid to the shared /api/me store, which loads the numbers once per sign-in (that call
// also makes the backend create or update the user's row, so it doubles as the "first sign-in"
// sync). Announcing the same uid twice is harmless. Called by the history store; does nothing in
// static mode.
export function setUsageOwner(uid: string | null) {
  if (!isApiMode) return;
  setMeOwner(uid);
}

// Re-reads this month's usage from GET /api/me (api mode only; a no-op in static mode or when
// signed out). Call it after a scan finishes or history is cleared. It is "quiet": if the request
// fails it keeps the last known numbers and says nothing. A response that arrives after a newer
// request or a user change is thrown away.
export async function refreshUsage(): Promise<void> {
  await loadMe({ quiet: true });
}
