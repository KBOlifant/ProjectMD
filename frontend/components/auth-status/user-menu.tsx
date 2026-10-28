"use client";

import { useRouter } from "next/navigation";
import { useEffect, useId, useRef, useState } from "react";
import { getIdToken, signOutUser } from "@/app/functions/auth/auth-service";
import { useAuth } from "@/app/functions/auth/useAuth";
import type { Role } from "@/app/functions/rbac/roles";
import { Button } from "@/components/ui/button";

// Turns an email into a 2-letter avatar label: first letters of the first two name parts
// ("jane.doe@x.com" -> "JD"), or the first two characters ("kb@x.com" -> "KB"). Falls back to "?".
function emailInitials(email: string | null): string {
  const parts = (email ?? "").split("@")[0].split(/[^a-z0-9]+/i).filter(Boolean);
  const letters = parts.length > 1 ? parts[0][0] + parts[1][0] : (parts[0] ?? "").slice(0, 2);
  return letters.toUpperCase() || "?";
}

// Round-cornered tile with the user's initials. Purely presentational; stores nothing.
function Avatar({ email }: { email: string | null }) {
  return (
    <span
      aria-hidden="true"
      className="grid size-9 flex-none place-items-center rounded-md border border-violet-500/40 bg-violet-700 font-mono text-[12px] font-semibold text-ink"
    >
      {emailInitials(email)}
    </span>
  );
}

// Small chip with the user's role name (viewer / member / admin). The role is display only here;
// what each role may do is decided by the rbac helpers. Stores nothing.
function RoleBadge({ role }: { role: Role }) {
  const tone =
    role === "admin"
      ? "border-violet-500/60 bg-violet-700 text-ink"
      : role === "viewer"
        ? "border-border-strong bg-bg-raised text-warning"
        : "border-border-strong bg-bg-raised text-ink-muted";
  return <span className={`ts-label inline-block rounded-sm border px-1.5 py-0.5 ${tone}`}>{role}</span>;
}

// Development-only helper: copies a fresh Firebase ID token to the clipboard so it can be pasted into
// the backend's Swagger page (Authorize). The token is never shown, stored or logged here. Only
// "idle / copied / failed" is temporary state. It is not rendered in production builds.
function CopyTokenButton() {
  const [state, setState] = useState<"idle" | "copied" | "failed">("idle");

  // Gets a fresh token and puts it on the clipboard; shows the result for two seconds.
  async function copyToken() {
    try {
      const token = await getIdToken(true);
      if (!token) throw new Error("no token");
      await navigator.clipboard.writeText(token);
      setState("copied");
    } catch {
      setState("failed");
    }
    setTimeout(() => setState("idle"), 2000);
  }

  return (
    <Button variant="ghost" className="mt-2 w-full" onClick={copyToken} title="Dev only: paste into Swagger > Authorize">
      {state === "copied" ? "Token copied" : state === "failed" ? "Copy failed" : "Copy API token"}
    </Button>
  );
}

// Account card: avatar, truncated email, role chip and a "Sign out" button. Signing out calls
// signOutUser() and then goes to /login; if that fails it says so and re-enables the button.
// Only the pending/failed flags are temporary state; nothing is stored.
function AccountCard() {
  const router = useRouter();
  const { user } = useAuth();
  const [pending, setPending] = useState(false);
  const [failed, setFailed] = useState(false);
  if (!user) return null;

  // Signs the user out of Firebase, then sends them to the login screen.
  async function signOut() {
    setPending(true);
    setFailed(false);
    try {
      await signOutUser();
      router.replace("/login");
    } catch {
      setFailed(true);
      setPending(false);
    }
  }

  return (
    <div>
      <div className="flex items-center gap-3">
        <Avatar email={user.email} />
        <div className="min-w-0 flex-1">
          <p className="ts-small truncate text-ink" title={user.email ?? undefined}>
            {user.email ?? "Signed in"}
          </p>
          <RoleBadge role={user.role} />
        </div>
      </div>
      <Button variant="ghost" className="mt-3 w-full" onClick={signOut} disabled={pending}>
        Sign out
      </Button>
      {process.env.NODE_ENV === "development" && <CopyTokenButton />}
      {failed && (
        <p role="alert" className="ts-small mt-2 text-danger">
          Could not sign out. Try again.
        </p>
      )}
    </div>
  );
}

// Account menu used in both frames: an avatar button that opens a small panel with the account card
// (email, role, Sign out). `placement` says where the panel opens: "rail" beside the desktop icon
// rail, "top-bar" under the mobile top bar. It is a disclosure (aria-expanded): Escape closes it and
// returns focus to the button, and so does a click or focus move outside it. Renders nothing when
// nobody is signed in. `open` is temporary state; nothing is stored.
export function UserMenuCompact({ placement = "top-bar" }: { placement?: "rail" | "top-bar" }) {
  const { user } = useAuth();
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const panelId = useId();

  // While the panel is open, close it on Escape, an outside click, or focus leaving it.
  useEffect(() => {
    if (!open) return;
    const inside = (target: EventTarget | null) => target instanceof Node && !!rootRef.current?.contains(target);
    const onPointerDown = (e: PointerEvent) => {
      if (!inside(e.target)) setOpen(false);
    };
    const onFocusIn = (e: FocusEvent) => {
      if (!inside(e.target)) setOpen(false);
    };
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      setOpen(false);
      buttonRef.current?.focus();
    };
    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("focusin", onFocusIn);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("focusin", onFocusIn);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  if (!user) return null;

  return (
    <div ref={rootRef} className="relative">
      <button
        ref={buttonRef}
        type="button"
        aria-label="Account menu"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((v) => !v)}
        className="rounded-md"
      >
        <Avatar email={user.email} />
      </button>
      {open && (
        <div
          id={panelId}
          className={`absolute z-40 w-64 rounded-lg border border-border-strong bg-bg-surface p-3 shadow-panel ${
            placement === "rail" ? "bottom-0 left-full ml-3" : "right-0 top-full mt-2"
          }`}
        >
          <AccountCard />
        </div>
      )}
    </div>
  );
}
