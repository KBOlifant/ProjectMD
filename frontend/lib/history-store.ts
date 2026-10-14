"use client";

import { useSyncExternalStore } from "react";
import { apiErrorMessage, apiFetch } from "@/app/functions/data/api-client";
import { isApiMode } from "@/app/functions/data/mode";
import { refreshUsage, setUsageOwner } from "@/app/functions/data/usage-store";
import type { Scan } from "./types";

// Two modes, chosen once by NEXT_PUBLIC_SCAN_PROVIDER (see app/functions/data/mode.ts):
//  - static (default): history lives in this browser's localStorage, one key per signed-in user.
//  - api: the backend is the source of truth. Signing in loads GET /api/scans into the in-memory
//    cache; addScan only updates the cache (the server already saved the scan); clearHistory calls
//    DELETE /api/scans first. Nothing is written to localStorage in this mode.

// localStorage key prefix (static mode). Each signed-in user's history is stored under
// `projectmd.history.<uid>` as one JSON array of Scan objects, so two accounts in one browser never
// see each other's scans. The old shared `projectmd.history` key is deliberately left alone.
const KEY_PREFIX = "projectmd.history.";
const EMPTY: Scan[] = [];
const listeners = new Set<() => void>();
// Whose history is currently loaded (the signed-in user's uid), or null when signed out.
// Set by the auth provider through setHistoryOwner.
let owner: string | null = null;
// In-memory copy of the owner's history so we only parse localStorage (or ask the server) once per owner.
let cache: Scan[] | null = null;

// Loading/error state of the backend history (api mode). Static mode always stays idle.
interface HistoryStatus {
  loading: boolean;
  error: string | null;
}
const IDLE_STATUS: HistoryStatus = { loading: false, error: null };
let status: HistoryStatus = IDLE_STATUS;
// Bumped whenever the owner changes, a load starts or history is cleared, so a slow response for an
// old owner or an old request is thrown away instead of overwriting newer data.
let loadId = 0;
// Scans added (addScan) since the current load began, so a list response that was fetched before
// the new scan was saved does not make it disappear.
let pendingAdds: Scan[] = [];

// Returns the localStorage key for the current owner, or null when nobody is signed in. Stores nothing.
function storageKey(): string | null {
  return owner === null ? null : KEY_PREFIX + owner;
}

// Calls every subscribed component so it re-renders. Stores nothing.
function notify() {
  listeners.forEach((l) => l());
}

// Returns the saved scans (newest first) for the current owner. With no owner it returns an
// empty list. Api mode returns the in-memory copy (empty until the server list has loaded).
// Static mode loads and parses the JSON from localStorage into `cache` on first call; if it is
// missing, corrupt or storage is blocked it falls back to []. Stores nothing.
function read(): Scan[] {
  if (isApiMode) return cache ?? EMPTY;
  const key = storageKey();
  if (key === null) return EMPTY;
  if (cache === null) {
    try {
      const parsed = JSON.parse(localStorage.getItem(key) ?? "[]");
      cache = Array.isArray(parsed) ? parsed : [];
    } catch {
      cache = [];
    }
  }
  return cache;
}

// Replaces the current owner's whole history. Stores the new array in the in-memory cache and,
// in static mode, also saves it to localStorage as JSON (if the browser blocks storage, only the
// cache is kept). Then notifies every subscribed component. With no owner it does nothing.
function write(next: Scan[]) {
  const key = storageKey();
  if (key === null) return;
  cache = next;
  if (!isApiMode) {
    try {
      localStorage.setItem(key, JSON.stringify(next));
    } catch {
      // storage blocked: keep the in-memory copy
    }
  }
  notify();
}

// Registers a callback to run whenever the history changes (used by useSyncExternalStore).
// Returns a function that removes the callback. Stores nothing persistent.
function subscribe(cb: () => void) {
  listeners.add(cb);
  return () => {
    listeners.delete(cb);
  };
}

// Api mode: fetches the owner's scans with GET /api/scans and puts them in the cache. While it runs
// `loading` is true (so pages show nothing instead of "No scans yet"); if it fails `error` holds a
// message and the list stays as it was. The result is ignored if the user changed, history was
// cleared or a newer load started in the meantime.
async function loadRemote(): Promise<void> {
  const forOwner = owner;
  const mine = ++loadId;
  pendingAdds = [];
  status = { loading: true, error: null };
  notify();
  try {
    const rows = await apiFetch<Scan[]>("/api/scans");
    if (loadId !== mine || owner !== forOwner) return;
    const list = Array.isArray(rows) ? rows : [];
    const known = new Set(list.map((s) => s.id));
    cache = [...pendingAdds.filter((s) => !known.has(s.id)), ...list];
    pendingAdds = [];
    status = IDLE_STATUS;
  } catch (err) {
    if (loadId !== mine || owner !== forOwner) return;
    status = { loading: false, error: apiErrorMessage(err, "Could not load your scans. Try again.") };
  }
  notify();
}

// Tells the store whose history to use (the signed-in user's uid, or null when signed out).
// Changing the owner clears the in-memory copy and notifies subscribers. Static mode then loads
// that user's localStorage key on the next read; api mode starts loading their scans from the
// backend (and their usage, which also lets the backend create the user's row). With null the
// history is empty and nothing is written. Called by the auth provider; stores nothing itself.
export function setHistoryOwner(uid: string | null) {
  if (uid === owner) return;
  owner = uid;
  cache = null;
  pendingAdds = [];
  loadId += 1;
  status = IDLE_STATUS;
  if (isApiMode) {
    setUsageOwner(uid);
    if (uid !== null) void loadRemote();
  }
  notify();
}

// Returns the uid whose history is currently loaded, or null. Lets the scan provider check that
// the user did not change while a scan was being generated. Stores nothing.
export function getHistoryOwner(): string | null {
  return owner;
}

// Saves a finished scan at the top of the current owner's history. Static mode persists the full
// Scan (url, host, date, duration, confidence, pages, tech list and generated markdown) to
// localStorage. Api mode only updates the in-memory list, because the backend already saved it.
// Does nothing when signed out.
export function addScan(scan: Scan) {
  if (isApiMode) {
    pendingAdds.push(scan);
    if (read().some((s) => s.id === scan.id)) return;
  }
  write([scan, ...read()]);
}

// Deletes every saved scan for the current owner. Static mode overwrites their localStorage entry
// with an empty array. Api mode calls DELETE /api/scans first and only empties the list once the
// server confirmed; if that fails the list is kept and the returned promise rejects with a message
// that is ready to show. No permission check here; the UI gates it with RBAC ("history:clear").
export async function clearHistory(): Promise<void> {
  if (!isApiMode) {
    write([]);
    return;
  }
  const forOwner = owner;
  if (forOwner === null) return;
  try {
    await apiFetch<void>("/api/scans", { method: "DELETE" });
  } catch (err) {
    throw new Error(apiErrorMessage(err, "Could not clear your history. Try again."));
  }
  if (owner !== forOwner) return;
  // A list load still in flight might hold the scans that were just deleted: drop it.
  loadId += 1;
  pendingAdds = [];
  status = IDLE_STATUS;
  write([]);
  void refreshUsage();
}

// Api mode: tries loading the scans from the backend again (for a "Retry" button after a failed
// load). Does nothing in static mode or when signed out.
export function reloadHistory() {
  if (isApiMode && owner !== null) void loadRemote();
}

// React hook that returns the saved scans and re-renders when they change.
// Server render gets an empty list so it matches the first client paint. Stores nothing.
export function useHistory(): Scan[] {
  return useSyncExternalStore(subscribe, read, () => EMPTY);
}

// Returns whether the history is ready to show (false while the first api-mode load is running).
function getReady(): boolean {
  return !status.loading;
}

// React hook that is false during server render / hydration (and, in api mode, while the scans are
// still loading from the backend) and true once the list can be shown. Used to avoid flashing
// "no scans yet" before the history has been read. Stores nothing.
export function useIsHydrated(): boolean {
  return useSyncExternalStore(subscribe, getReady, () => false);
}

// React hook for the api-mode load state: `loading` while the list is being fetched and `error`
// with a ready-to-show message when the last load failed. Always idle/no error in static mode.
export function useHistoryStatus(): HistoryStatus {
  return useSyncExternalStore(subscribe, () => status, () => IDLE_STATUS);
}

// The monthly quota constant now lives with the usage code; re-exported here for older imports.
export { USAGE_LIMIT } from "@/app/functions/data/usage-store";
