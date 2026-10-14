import { apiFetch } from "./api-client";
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
const listeners = new Set<() => void>();
// The signed-in user's uid (set through the history store), the latest usage numbers, and a counter
// that makes older responses lose against newer ones or against a user change.
let owner: string | null = null;
let state: Usage = { used: 0, limit: USAGE_LIMIT, loading: false };
let requestId = 0;

// Replaces the usage numbers and tells every subscribed component. Does nothing if nothing changed.
function setState(next: Usage) {
  if (next.used === state.used && next.limit === state.limit && next.loading === state.loading) return;
  state = next;
  listeners.forEach((l) => l());
}

// Registers a callback to run when the usage changes (used by useSyncExternalStore). Returns a
// function that removes it.
export function subscribeUsage(cb: () => void) {
  listeners.add(cb);
  return () => {
    listeners.delete(cb);
  };
}

// Returns the current usage numbers (the same object until they change).
export function getUsageSnapshot(): Usage {
  return state;
}

// Returns the usage the server render should use.
export function getServerUsageSnapshot(): Usage {
  return SERVER_SNAPSHOT;
}

// Tells the store whose usage to track (the signed-in uid, or null when signed out). In api mode a
// new user resets the numbers and loads them with GET /api/me; that call also makes the backend
// create or update the user's row, so it doubles as the "first sign-in" sync. Failures are silent.
// Called by the history store; does nothing in static mode.
export function setUsageOwner(uid: string | null) {
  if (!isApiMode || uid === owner) return;
  owner = uid;
  requestId += 1;
  setState({ used: 0, limit: USAGE_LIMIT, loading: uid !== null });
  if (uid !== null) void refreshUsage();
}

// Re-reads this month's usage from GET /api/me (api mode only; a no-op in static mode or when
// signed out). Call it after a scan finishes or history is cleared. If the request fails it keeps
// the last known numbers and says nothing. A response that arrives after a newer request or a user
// change is thrown away.
export async function refreshUsage(): Promise<void> {
  if (!isApiMode || owner === null) return;
  const forOwner = owner;
  const mine = ++requestId;
  try {
    const me = await apiFetch<{ usage?: { used?: unknown; limit?: unknown } }>("/api/me");
    if (requestId !== mine || owner !== forOwner) return;
    const used = me?.usage?.used;
    const limit = me?.usage?.limit;
    setState({
      used: typeof used === "number" && used >= 0 ? used : state.used,
      limit: typeof limit === "number" && limit > 0 ? limit : state.limit,
      loading: false,
    });
  } catch {
    if (requestId !== mine || owner !== forOwner) return;
    setState({ ...state, loading: false });
  }
}
