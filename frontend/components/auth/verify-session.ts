// Remembers, for this browser tab only, when the verification email was last sent and whether it
// went out. The sign-up screen records it, and the verify-email screen reads it to carry on the
// 60-second resend countdown (or to offer a resend if the first email failed). It is a tiny
// subscribable store so a screen that is already open updates the moment something is recorded.
// Only a timestamp and a yes/no flag are kept (in sessionStorage); no email address or token.

const STORAGE_KEY = "projectmd:verify-email-sent";

export const RESEND_COOLDOWN_SECONDS = 60;

export interface VerificationSend {
  sentAt: number;
  ok: boolean;
}

const listeners = new Set<() => void>();
let memory = "";

// Tells every subscribed screen that the stored value changed.
function notify() {
  listeners.forEach((listener) => listener());
}

// Notes "a verification email was just sent (or failed to send)" with the current time.
export function recordVerificationSend(ok: boolean): void {
  memory = `${Date.now()}:${ok ? 1 : 0}`;
  try {
    window.sessionStorage.setItem(STORAGE_KEY, memory);
  } catch {
    // storage blocked: the in-memory copy still works for this page load
  }
  notify();
}

// Forgets the record (used on sign out so one person's cooldown never carries to another account).
export function clearVerificationSend(): void {
  memory = "";
  try {
    window.sessionStorage.removeItem(STORAGE_KEY);
  } catch {
    // nothing to clear
  }
  notify();
}

// Lets a component listen for changes (used with useSyncExternalStore). Returns the unsubscribe function.
export function subscribeVerificationSend(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

// Current stored value as a plain string ("" when nothing is recorded). A string is returned so
// React can compare snapshots by value.
export function getVerificationSendSnapshot(): string {
  try {
    return window.sessionStorage.getItem(STORAGE_KEY) ?? memory;
  } catch {
    return memory;
  }
}

// Snapshot used while rendering on the server, where nothing has been recorded.
export function getVerificationSendServerSnapshot(): string {
  return "";
}

// Turns the stored string back into { sentAt, ok }, or null when nothing (or junk) is stored.
export function parseVerificationSend(raw: string): VerificationSend | null {
  const [time, flag] = raw.split(":");
  const sentAt = Number(time);
  if (!Number.isFinite(sentAt) || sentAt <= 0) return null;
  return { sentAt, ok: flag === "1" };
}
