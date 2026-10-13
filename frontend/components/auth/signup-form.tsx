"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useRef, useState, type FormEvent } from "react";
import { signUp } from "@/app/functions/auth/auth-service";
import { authErrorMessage } from "@/app/functions/auth/errors";
import { Button } from "@/components/ui/button";
import { TextField } from "@/components/ui/text-field";
import { AuthCard } from "./auth-card";
import { FormMessage } from "./form-message";
import { PasswordField } from "./password-field";
import { SocialProviders } from "./social-providers";
import { useTouched } from "./use-touched";
import { recordVerificationSend } from "./verify-session";
import { MIN_PASSWORD_LENGTH, confirmPasswordError, emailError, newPasswordError } from "./validation";

// The /signup screen: email, password and confirm password. Checks them in the browser (valid email,
// at least 8 characters, passwords match), creates the account, then goes to /verify-email.
// Typed values, errors and the pending flag are temporary state; passwords are never stored or logged.
export function SignupForm() {
  const router = useRouter();
  const emailRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const confirmRef = useRef<HTMLInputElement>(null);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [formError, setFormError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const touched = useTouched<"email" | "password" | "confirm">();

  const errors = {
    email: emailError(email),
    password: newPasswordError(password),
    confirm: confirmPasswordError(password, confirm),
  };

  // Runs when the form is submitted: checks every field (focusing the first bad one), then creates
  // the account. The sign-up call also sends the verification email; we note the outcome so the verify
  // screen can run its resend countdown. A failure shows a safe message from authErrorMessage.
  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (pending) return;
    touched.submit();
    setFormError(null);
    if (errors.email) return emailRef.current?.focus();
    if (errors.password) return passwordRef.current?.focus();
    if (errors.confirm) return confirmRef.current?.focus();

    setPending(true);
    try {
      const { verificationEmailSent } = await signUp(email.trim(), password);
      recordVerificationSend(verificationEmailSent);
      router.replace("/verify-email");
    } catch (error) {
      setFormError(authErrorMessage(error, "signup"));
      setPending(false);
      emailRef.current?.focus();
    }
  }

  return (
    <AuthCard
      title="Create your account"
      subtitle="Turn any public URL into structured Markdown docs. We will email you a link to verify your address."
      footer={
        <>
          Already have an account?{" "}
          <Link href="/login" className="text-violet-400 hover:text-ink">
            Sign in
          </Link>
        </>
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
          error={touched.shown("email") ? errors.email : null}
        />
        <PasswordField
          ref={passwordRef}
          label="Password"
          name="password"
          autoComplete="new-password"
          required
          hint={`At least ${MIN_PASSWORD_LENGTH} characters.`}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          onBlur={() => touched.touch("password", password)}
          error={touched.shown("password") ? errors.password : null}
        />
        <PasswordField
          ref={confirmRef}
          label="Confirm password"
          name="confirm-password"
          autoComplete="new-password"
          required
          value={confirm}
          onChange={(e) => setConfirm(e.target.value)}
          onBlur={() => touched.touch("confirm", confirm)}
          error={touched.shown("confirm") ? errors.confirm : null}
        />
        <FormMessage message={formError} />
        <Button type="submit" className="w-full" disabled={pending}>
          {pending ? "Creating account…" : "Create account"}
        </Button>
      </form>
      <SocialProviders />
    </AuthCard>
  );
}
