"use client";

import { useAuth } from "@/app/functions/auth/useAuth";
import { reloadMe, useMeState } from "@/app/functions/data/me-store";

// Strip shown at the top of the workspace (server mode only) when the backend could not tell us the
// signed-in user's role. Until it answers they keep working as a read-only "viewer". "Retry" asks
// the backend again and is disabled while that request runs. Renders nothing otherwise. Same style
// as the verify-email banner. Reads the auth state; stores nothing.
export function ServerNotice() {
  const { status, error } = useAuth();
  const { busy } = useMeState();
  if (status !== "authed" || !error) return null;

  return (
    <div role="status" className="border-b border-border-subtle bg-bg-raised px-4 py-3 md:px-8">
      <div className="mx-auto flex w-full max-w-[1040px] flex-wrap items-center gap-x-3 gap-y-1">
        <span aria-hidden="true" className="size-1.5 flex-none rounded-full bg-warning" />
        <p className="ts-body text-ink-body">Can&apos;t reach the server. You have read-only access until it is back.</p>
        <button
          type="button"
          onClick={() => void reloadMe()}
          disabled={busy}
          className="ts-mono-ui rounded-sm text-violet-400 hover:text-ink-hover disabled:text-ink-dim"
        >
          {busy ? "Retrying…" : "Retry"}
        </button>
      </div>
    </div>
  );
}
