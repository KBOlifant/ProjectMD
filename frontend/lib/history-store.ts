"use client";

import { useSyncExternalStore } from "react";
import type { Scan } from "./types";

// localStorage key prefix. Each signed-in user's history is stored under `projectmd.history.<uid>`
// as one JSON array of Scan objects, so two accounts in one browser never see each other's scans.
// The old shared `projectmd.history` key is deliberately left alone (not migrated or deleted).
const KEY_PREFIX = "projectmd.history.";
const EMPTY: Scan[] = [];
const listeners = new Set<() => void>();
// Whose history is currently loaded (the signed-in user's uid), or null when signed out.
// Set by the auth provider through setHistoryOwner.
let owner: string | null = null;
// In-memory copy of the owner's history so we only parse localStorage once per owner.
let cache: Scan[] | null = null;

// Returns the localStorage key for the current owner, or null when nobody is signed in. Stores nothing.
function storageKey(): string | null {
  return owner === null ? null : KEY_PREFIX + owner;
}

// Returns the saved scans (newest first) for the current owner. With no owner it returns an
// empty list. On first call for an owner it loads and parses the JSON from localStorage into
// `cache`; if it is missing, corrupt or storage is blocked it falls back to []. Stores nothing.
function read(): Scan[] {
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

// Replaces the current owner's whole history. Stores the new array in the in-memory cache AND
// saves it to localStorage as JSON (if the browser blocks storage, only the cache is kept). Then
// notifies every subscribed component so they re-render. With no owner it does nothing.
function write(next: Scan[]) {
  const key = storageKey();
  if (key === null) return;
  cache = next;
  try {
    localStorage.setItem(key, JSON.stringify(next));
  } catch {
    // storage blocked: keep the in-memory copy
  }
  listeners.forEach((l) => l());
}

// Registers a callback to run whenever the history changes (used by useSyncExternalStore).
// Returns a function that removes the callback. Stores nothing persistent.
function subscribe(cb: () => void) {
  listeners.add(cb);
  return () => {
    listeners.delete(cb);
  };
}

// Tells the store whose history to use (the signed-in user's uid, or null when signed out).
// Changing the owner clears the in-memory copy, makes the next read load that user's key from
// localStorage, and notifies subscribers. With null the history is empty and nothing is written.
// Called by the auth provider; stores nothing itself.
export function setHistoryOwner(uid: string | null) {
  if (uid === owner) return;
  owner = uid;
  cache = null;
  listeners.forEach((l) => l());
}

// Returns the uid whose history is currently loaded, or null. Lets the scan provider check that
// the user did not change while a scan was being generated. Stores nothing.
export function getHistoryOwner(): string | null {
  return owner;
}

// Saves a finished scan at the top of the current owner's history. Persists the full Scan
// (url, host, date, duration, confidence, pages, tech list and generated markdown) to localStorage.
// Does nothing when signed out.
export function addScan(scan: Scan) {
  write([scan, ...read()]);
}

// Deletes every saved scan for the current owner. Overwrites their localStorage entry with an
// empty array. No permission check here; the UI gates it with RBAC ("history:clear").
export function clearHistory() {
  write([]);
}

// React hook that returns the saved scans and re-renders when they change.
// Server render gets an empty list so it matches the first client paint. Stores nothing.
export function useHistory(): Scan[] {
  return useSyncExternalStore(subscribe, read, () => EMPTY);
}

const noopSubscribe = () => () => {};
// React hook that is false during server render / hydration and true once running in the browser.
// Used to avoid flashing "no scans yet" before localStorage has been read. Stores nothing.
export function useIsHydrated(): boolean {
  return useSyncExternalStore(noopSubscribe, () => true, () => false);
}

export const USAGE_LIMIT = 10; // placeholder quota until billing exists
