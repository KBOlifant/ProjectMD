"use client";

import { useSyncExternalStore } from "react";
import { isRole, type Role } from "@/app/functions/rbac/roles";
import { apiFetch } from "./api-client";
import { isApiMode } from "./mode";

// Holds the one answer of GET /api/me for the signed-in user (api mode only). The backend returns
// the user's DATABASE role plus this month's usage, so this single store feeds both the auth
// provider (the role) and the usage store (the numbers): one request per sign-in or refresh, not
// two. It is cleared when the user changes or signs out, and a response that arrives after a newer
// request or a user change is thrown away. In static mode every function here does nothing.
// Nothing is persisted and no token or response body is ever logged.

// What GET /api/me says about the signed-in user. `role` is already checked (an unknown value
// becomes "viewer"). usage.used is only kept when it is a valid number; there is no monthly cap.
export interface Me {
  uid: string | null;
  email: string | null;
  emailVerified: boolean;
  role: Role;
  usage: { used?: number };
}

// The store's state. `owner` is the uid it belongs to. `loaded` turns true once the first request
// for that owner has finished (success or failure). `me` is the last good answer. `error` is true
// when the latest non-quiet request failed. `busy` is true while a request is running.
export interface MeState {
  owner: string | null;
  loaded: boolean;
  me: Me | null;
  error: boolean;
  busy: boolean;
}

const INITIAL: MeState = { owner: null, loaded: false, me: null, error: false, busy: false };
const listeners = new Set<() => void>();
let state: MeState = INITIAL;
// Counter that makes older responses lose against newer requests or against a user change, and the
// request currently running (so overlapping callers share it instead of sending a second one).
let requestId = 0;
let inflight: Promise<void> | null = null;

// Replaces part of the state and tells every subscribed component. Stores nothing else.
function update(patch: Partial<MeState>) {
  state = { ...state, ...patch };
  listeners.forEach((l) => l());
}

// Returns the value when it is a finite number of at least `min`, otherwise undefined.
function count(value: unknown, min: number): number | undefined {
  return typeof value === "number" && Number.isFinite(value) && value >= min ? value : undefined;
}

// Turns the raw GET /api/me body into a Me, failing closed: an unknown or missing role becomes
// "viewer". A body that is not an object is rejected (the caller treats that as a failed request).
function parseMe(raw: unknown): Me {
  if (typeof raw !== "object" || raw === null) throw new Error("Unexpected /api/me response");
  const f = raw as Record<string, unknown>;
  const usage = typeof f.usage === "object" && f.usage !== null ? (f.usage as Record<string, unknown>) : {};
  return {
    uid: typeof f.uid === "string" ? f.uid : null,
    email: typeof f.email === "string" ? f.email : null,
    emailVerified: f.emailVerified === true,
    role: isRole(f.role) ? f.role : "viewer",
    usage: { used: count(usage.used, 0) },
  };
}

// Registers a callback to run when the state changes (used by useSyncExternalStore). Returns a
// function that removes it.
export function subscribeMe(cb: () => void) {
  listeners.add(cb);
  return () => {
    listeners.delete(cb);
  };
}

// Returns the current state (the same object until something changes).
export function getMeSnapshot(): MeState {
  return state;
}

// Returns the state the server render should use: nothing loaded.
export function getServerMeSnapshot(): MeState {
  return INITIAL;
}

// React hook that returns the /api/me state and re-renders when it changes. Stores nothing.
export function useMeState(): MeState {
  return useSyncExternalStore(subscribeMe, getMeSnapshot, getServerMeSnapshot);
}

// Asks GET /api/me for the signed-in user. Overlapping calls share the request already running
// unless `force` is set (a forced call starts a new request and the older answer is ignored). A
// `quiet` call (used for usage refreshes) keeps the previous answer if it fails; any other failure
// marks the state as `error`, which the auth provider turns into the safe "viewer" role. Resolves
// when the request has finished and never rejects. Does nothing in static mode or when signed out.
export function loadMe({ quiet = false, force = false }: { quiet?: boolean; force?: boolean } = {}): Promise<void> {
  if (!isApiMode || state.owner === null) return Promise.resolve();
  if (inflight && !force) return inflight;

  const forOwner = state.owner;
  const mine = ++requestId;
  const run: Promise<void> = (async () => {
    try {
      const me = parseMe(await apiFetch<unknown>("/api/me"));
      if (requestId !== mine || state.owner !== forOwner) return;
      update({ me, loaded: true, error: false, busy: false });
    } catch {
      if (requestId !== mine || state.owner !== forOwner) return;
      if (quiet && state.loaded) update({ busy: false });
      else update({ loaded: true, error: true, busy: false });
    } finally {
      // Still the newest request: nothing is running any more.
      if (requestId === mine) inflight = null;
    }
  })();
  inflight = run;
  update({ busy: true });
  return run;
}

// Forces a fresh GET /api/me and marks the state as `error` if it fails. Used by the "Retry" button
// and by the auth provider's refresh(), so a changed role shows up. Never rejects.
export function reloadMe(): Promise<void> {
  return loadMe({ force: true });
}

// Tells the store whose answer to hold (the signed-in uid, or null when signed out). A different
// owner clears everything, drops any request still in flight and starts one new GET /api/me for the
// new user (that call also lets the backend create or update the user's row). Setting the same
// owner again does nothing, so several callers can announce the same sign-in safely. Does nothing
// in static mode.
export function setMeOwner(uid: string | null) {
  if (!isApiMode || uid === state.owner) return;
  requestId += 1;
  inflight = null;
  update({ ...INITIAL, owner: uid });
  if (uid !== null) void loadMe();
}
