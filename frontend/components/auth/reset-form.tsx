"use client";

import Link from "next/link";
import { useRef, useState, type FormEvent } from "react";
import { sendPasswordReset } from "@/app/functions/auth/auth-service";
import { RESET_SUCCESS_MESSAGE, authErrorMessage } from "@/app/functions/auth/errors";
import { Button } from "@/components/ui/button";
import { TextField } from "@/components/ui/text-field";
import { AuthCard } from "./auth-card";
import { FormMessage } from "./form-message";
import { useTouched } from "./use-touched";
import { emailError } from "./validation";

// The /reset screen: email plus a Send reset link button. After sending it always shows the same
// "If an account exists..." text, whether or not the email has an account, so nobody can probe which
// emails are registered. Real problems (offline, too many attempts) still show their own message.
// Typed email, messages and the pending flag are temporary state.
export function ResetForm() {
  const emailRef = useRef<HTMLInputElement>(null);
  const [email, setEmail] = useState("");
  const [sent, setSent] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const touched = useTouched<"email">();

  const error = emailError(email);

  // Runs when the form is submitted: checks the email, asks Firebase to send the reset link, and shows
  // the neutral success text. An unknown account counts as success on purpose.
  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (pending) return;
    touched.submit();
    setFormError(null);
    setSent(false);
    if (error) return emailRef.current?.focus();

    setPending(true);
    try {
      await sendPasswordReset(email.trim());
      setSent(true);
    } catch (e) {
      const message = authErrorMessage(e, "reset");
      if (message === RESET_SUCCESS_MESSAGE) setSent(true);
      else setFormError(message);
    } finally {
      setPending(false);
    }
  }

  return (
    <AuthCard
      title="Reset your password"
      subtitle="Enter your email and we will send you a link to choose a new password."
      footer={
        <Link href="/login" className="text-violet-400 hover:text-ink-hover">
          Back to sign in
        </Link>
      }
    >
      <form noValidate onSubmit={onSubmit} className="space-y-4">
        <TextField
          ref={emailRef}
          label="Email"
          name="email"
          type="email"
          inputMode="email"
          autoComplete="email"
          autoCapitalize="none"
          spellCheck={false}
          required
          placeholder="you@company.com"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          onBlur={() => touched.touch("email", email)}
          error={touched.shown("email") ? error : null}
        />
        <FormMessage message={sent ? RESET_SUCCESS_MESSAGE : formError} tone={sent ? "success" : "error"} />
        <Button type="submit" className="w-full" disabled={pending}>
          {pending ? "Sending…" : "Send reset link"}
        </Button>
      </form>
    </AuthCard>
  );
}
