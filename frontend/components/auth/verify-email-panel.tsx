"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState, useSyncExternalStore } from "react";
import { sendVerificationEmail, signOutUser } from "@/app/functions/auth/auth-service";
import { authErrorMessage } from "@/app/functions/auth/errors";
import { useAuth } from "@/app/functions/auth/useAuth";
import { Button } from "@/components/ui/button";
import { AuthCard } from "./auth-card";
import { FormMessage, type FormMessageTone } from "./form-message";
import {
  RESEND_COOLDOWN_SECONDS,
  clearVerificationSend,
  getVerificationSendServerSnapshot,
  getVerificationSendSnapshot,
  parseVerificationSend,
  recordVerificationSend,
  subscribeVerificationSend,
} from "./verify-session";

// How often (ms) to quietly re-check whether the email has been verified while the tab is visible.
const POLL_INTERVAL_MS = 5000;

interface Notice {
  text: string;
  tone: FormMessageTone;
}

// The /verify-email screen for signed-in users whose email is not verified yet. It shows the address
// the link went to, explains that scans unlock after verifying, and offers Resend email (with a
// 60-second countdown), "I've verified" and Sign out. While the tab is visible it re-checks every
// ~5 seconds; once verified, the sign-in guard moves the user on to the app automatically. Signed-out
// visitors are sent to /login. Countdown, notices and pending flags are temporary state; the last
// send time comes from verify-session.ts.
export function VerifyEmailPanel() {
  const router = useRouter();
  const { status, user, refresh } = useAuth();
  const sendRecord = parseVerificationSend(
    useSyncExternalStore(subscribeVerificationSend, getVerificationSendSnapshot, getVerificationSendServerSnapshot),
  );
  const [now, setNow] = useState(() => Date.now());
  const [notice, setNotice] = useState<Notice | null>(null);
  const [resending, setResending] = useState(false);
  const [checking, setChecking] = useState(false);
  const [signingOut, setSigningOut] = useState(false);

  // Seconds left before Resend is allowed again. Only a send that really went out starts a countdown.
  const cooldownEnd = sendRecord?.ok ? sendRecord.sentAt + RESEND_COOLDOWN_SECONDS * 1000 : 0;
  const remaining = Math.min(RESEND_COOLDOWN_SECONDS, Math.max(0, Math.ceil((cooldownEnd - now) / 1000)));

  // Signed-out visitors have no business here: send them to the login screen.
  useEffect(() => {
    if (status === "anon") router.replace("/login");
  }, [status, router]);

  // Ticks once a second while a countdown is running, and stops by itself when it reaches zero.
  useEffect(() => {
    if (cooldownEnd === 0) return;
    const id = window.setInterval(() => {
      const t = Date.now();
      setNow(t);
      if (t >= cooldownEnd) window.clearInterval(id);
    }, 1000);
    return () => window.clearInterval(id);
  }, [cooldownEnd]);

  // While the tab is visible, re-reads the user from Firebase every few seconds (and right when the
  // tab comes back into view) so a verification done in another tab or on a phone is noticed. Network
  // errors are ignored here; the next tick simply tries again. Overlapping checks are skipped.
  useEffect(() => {
    if (status !== "authed") return;
    let busy = false;
    async function poll() {
      if (busy || document.visibilityState !== "visible") return;
      busy = true;
      try {
        await refresh();
      } catch {
        // try again on the next tick
      } finally {
        busy = false;
      }
    }
    const id = window.setInterval(poll, POLL_INTERVAL_MS);
    document.addEventListener("visibilitychange", poll);
    return () => {
      window.clearInterval(id);
      document.removeEventListener("visibilitychange", poll);
    };
  }, [status, refresh]);

  if (status !== "authed" || !user || user.emailVerified) return null;

  const resendLocked = resending || remaining > 0;
  const failedFirstSend: Notice | null =
    sendRecord && !sendRecord.ok
      ? { text: "We could not send the verification email. Select Resend email to try again.", tone: "error" }
      : null;
  const shown = notice ?? failedFirstSend;

  // Sends the verification email again, then starts the 60-second countdown. Ignored while locked.
  async function onResend() {
    if (resendLocked) return;
    setResending(true);
    setNotice(null);
    try {
      await sendVerificationEmail();
      recordVerificationSend(true);
      setNow(Date.now());
      setNotice({ text: "Verification email sent. Check your inbox.", tone: "success" });
    } catch (error) {
      setNotice({ text: authErrorMessage(error, "verify"), tone: "error" });
    } finally {
      setResending(false);
    }
  }

  // "I've verified": re-reads the user from Firebase. If the email is now verified the guard moves
  // the user on; otherwise it says "Not verified yet".
  async function onCheck() {
    if (checking) return;
    setChecking(true);
    setNotice(null);
    try {
      await refresh();
      setNotice({ text: "Not verified yet. Open the link in the email, then try again.", tone: "neutral" });
    } catch (error) {
      setNotice({ text: authErrorMessage(error, "verify"), tone: "error" });
    } finally {
      setChecking(false);
    }
  }

  // Signs out and forgets the resend countdown; the effect above then sends the user to /login.
  async function onSignOut() {
    if (signingOut) return;
    setSigningOut(true);
    try {
      clearVerificationSend();
      await signOutUser();
    } catch (error) {
      setNotice({ text: authErrorMessage(error, "verify"), tone: "error" });
      setSigningOut(false);
    }
  }

  return (
    <AuthCard
      title="Verify your email"
      subtitle={
        <>
          We sent a verification link to <strong className="font-semibold text-ink">{user.email ?? "your email address"}</strong>.
          Scans unlock once your email is verified.
        </>
      }
    >
      <p className="ts-small text-ink-muted">
        Open the link in that email, then come back. This page checks on its own. Nothing there? Look in your spam
        folder.
      </p>
      <div className="mt-6 space-y-3">
        <FormMessage message={shown?.text} tone={shown?.tone} />
        <Button className="w-full" onClick={() => void onCheck()} disabled={checking}>
          {checking ? "Checking…" : "I've verified"}
        </Button>
        <Button
          variant="secondary"
          className="w-full"
          onClick={() => void onResend()}
          aria-disabled={resendLocked ? true : undefined}
        >
          {resending ? "Sending…" : remaining > 0 ? `Resend email (${remaining}s)` : "Resend email"}
        </Button>
        <Button variant="ghost" className="w-full" onClick={() => void onSignOut()} disabled={signingOut}>
          Sign out
        </Button>
      </div>
    </AuthCard>
  );
}
