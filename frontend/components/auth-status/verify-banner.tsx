"use client";

import Link from "next/link";
import { useAuth } from "@/app/functions/auth/useAuth";

// Strip shown at the top of the workspace while the signed-in user's email is not verified. Says
// scans are blocked and links to /verify-email. Renders nothing for anyone else (loading, signed
// out, or already verified). Reads the user from useAuth(); stores nothing.
export function VerifyBanner() {
  const { status, user } = useAuth();
  if (status !== "authed" || !user || user.emailVerified) return null;

  return (
    <div role="status" className="border-b border-border-subtle bg-bg-raised px-4 py-3 md:px-8">
      <div className="mx-auto flex w-full max-w-[1040px] flex-wrap items-center gap-x-3 gap-y-1">
        <span aria-hidden="true" className="size-1.5 flex-none rounded-full bg-warning" />
        <p className="ts-body text-ink-body">Verify your email to start scans.</p>
        <Link href="/verify-email" className="ts-mono-ui text-violet-400 hover:text-ink">
          Verify email →
        </Link>
      </div>
    </div>
  );
}
