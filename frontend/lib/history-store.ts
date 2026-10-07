"use client";

import { useSyncExternalStore } from "react";
import type { Scan } from "./types";

// localStorage key. The whole history is stored under it as one JSON array of Scan objects.
const KEY = "projectmd.history";
const EMPTY: Scan[] = [];
const listeners = new Set<() => void>();
// In-memory copy of the history so we only parse localStorage once per page load.
let cache: Scan[] | null = null;

// Returns the saved scans (newest first). On first call it loads and parses the JSON
// from localStorage into `cache`; if it is missing or corrupt it falls back to [].
// Stores nothing.
function read(): Scan[] {
  if (cache === null) {
    try {
      const parsed = JSON.parse(localStorage.getItem(KEY) ?? "[]");
      cache = Array.isArray(parsed) ? parsed : [];
    } catch {
      cache = [];
    }
  }
  return cache;
}

// Replaces the whole history. Stores the new array in the in-memory cache AND saves it to
// localStorage as JSON (if the browser blocks storage, only the cache is kept). Then
// notifies every subscribed component so they re-render.
function write(next: Scan[]) {
  cache = next;
  try {
    localStorage.setItem(KEY, JSON.stringify(next));
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

// Saves a finished scan at the top of the history. Persists the full Scan
// (url, host, date, duration, confidence, pages, tech list and generated markdown) to localStorage.
export function addScan(scan: Scan) {
  write([scan, ...read()]);
}

// Deletes every saved scan. Overwrites the localStorage entry with an empty array.
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
