"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useRef, useState, type FormEvent } from "react";
import { signIn } from "@/app/functions/auth/auth-service";
import { authErrorMessage } from "@/app/functions/auth/errors";
import { safeNext } from "@/app/functions/auth/redirect";
import { Button } from "@/components/ui/button";
import { TextField } from "@/components/ui/text-field";
import { AuthCard } from "./auth-card";
import { FormMessage } from "./form-message";
import { PasswordField } from "./password-field";
import { SocialProviders } from "./social-providers";
import { useTouched } from "./use-touched";
import { emailError, loginPasswordError } from "./validation";

// The /login screen: email, password and a Sign in button, with links to sign up and reset. On
// success it goes to the `?next=` page (checked by safeNext) or "/". Typed values, errors and the
// pending flag are temporary state; the password is never stored or logged.
export function LoginForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const emailRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [formError, setFormError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const touched = useTouched<"email" | "password">();

  const errors = { email: emailError(email), password: loginPasswordError(password) };

  // Runs when the form is submitted: checks the fields (focusing the first bad one), then signs in.
  // A failure shows a safe message from authErrorMessage and keeps the email for another try.
  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (pending) return;
    touched.submit();
    setFormError(null);
    if (errors.email) return emailRef.current?.focus();
    if (errors.password) return passwordRef.current?.focus();

    setPending(true);
    try {
      await signIn(email.trim(), password);
      router.replace(safeNext(searchParams.get("next")));
    } catch (error) {
      setFormError(authErrorMessage(error, "login"));
      setPending(false);
      passwordRef.current?.focus();
    }
  }

  return (
    <AuthCard
      title="Sign in"
      subtitle="Welcome back. Sign in to generate and manage your docs."
      footer={
        <>
          New to Project MD?{" "}
          <Link href="/signup" className="text-violet-400 hover:text-ink">
            Create an account
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
          autoComplete="current-password"
          required
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          onBlur={() => touched.touch("password", password)}
          error={touched.shown("password") ? errors.password : null}
        />
        <div className="-mt-2 text-right">
          <Link href="/reset" className="ts-small text-violet-400 hover:text-ink">
            Forgot password?
          </Link>
        </div>
        <FormMessage message={formError} />
        <Button type="submit" className="w-full" disabled={pending}>
          {pending ? "Signing in…" : "Sign in"}
        </Button>
      </form>
      <SocialProviders />
    </AuthCard>
  );
}
