"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { safeNext } from "./redirect";
import { useAuth } from "./useAuth";

// These guards are UX only: they keep signed-out visitors off the workspace and signed-in users off
// the login screens. They protect nothing by themselves. The backend must verify the Firebase ID
// token (getIdToken()), the role and `email_verified` on every API call.

// Neutral full-screen placeholder shown while auth state is unknown or a redirect is in flight, so
// no workspace or form content flashes. Stores nothing.
function AuthSkeleton() {
  return (
    <div role="status" aria-busy="true" className="flex min-h-screen items-center justify-center bg-bg-base px-4">
      <span className="sr-only">Loading</span>
      <div className="w-full max-w-sm animate-pulse space-y-3" aria-hidden="true">
        <div className="h-6 w-32 rounded-md bg-bg-surface" />
        <div className="h-24 rounded-lg bg-bg-surface" />
        <div className="h-10 rounded-lg bg-bg-surface" />
      </div>
    </div>
  );
}

// Wraps pages that need a signed-in user. While auth is loading it shows the skeleton (no flash of
// content); signed-out visitors are sent to /login?next=<this page>; signed-in users see the
// children. Stores nothing.
export function RequireAuth({ children }: { children: ReactNode }) {
  const { status } = useAuth();
  const router = useRouter();
  const pathname = usePathname();

  useEffect(() => {
    if (status !== "anon") return;
    const path = pathname + window.location.search;
    router.replace("/login?next=" + encodeURIComponent(path));
  }, [status, pathname, router]);

  if (status !== "authed") return <AuthSkeleton />;
  return <>{children}</>;
}

// Wraps the login/signup/reset/verify screens. Signed-out visitors see them. A signed-in user is
// sent on to the `?next=` page (checked by safeNext) or "/". The one exception is a signed-in user
// whose email is not verified yet: they may stay on /verify-email. Someone who has just signed up
// (signed in, unverified, still on /signup) goes straight to /verify-email. Stores nothing.
export function RedirectIfAuthed({ children }: { children: ReactNode }) {
  const { status, user } = useAuth();
  const router = useRouter();
  const pathname = usePathname();

  const unverified = status === "authed" && user !== null && !user.emailVerified;
  const staysHere = unverified && pathname === "/verify-email";

  useEffect(() => {
    if (status !== "authed" || staysHere) return;
    if (unverified && pathname === "/signup") {
      router.replace("/verify-email");
      return;
    }
    const next = new URLSearchParams(window.location.search).get("next");
    router.replace(safeNext(next));
  }, [status, staysHere, unverified, pathname, router]);

  if (status === "loading" || (status === "authed" && !staysHere)) return <AuthSkeleton />;
  return <>{children}</>;
}
