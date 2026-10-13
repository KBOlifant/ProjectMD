"use client";

import { useState } from "react";
import { signInWithProvider } from "@/app/functions/auth/auth-service";
import { authErrorMessage } from "@/app/functions/auth/errors";
import { getEnabledSocialProviders, type AuthProviderId } from "@/app/functions/auth/providers";
import { Button } from "@/components/ui/button";
import { FormMessage } from "./form-message";

// Buttons for the enabled non-password sign-in providers, built from the registry in providers.ts.
// Today none are enabled, so this renders nothing. Once a provider is enabled there, a button
// appears here with no change to this file. On success the sign-in guard moves the user on. Only
// the pending flag and an error message are kept, temporarily, in this component.
export function SocialProviders() {
  const providers = getEnabledSocialProviders();
  const [pendingId, setPendingId] = useState<AuthProviderId | null>(null);
  const [error, setError] = useState<string | null>(null);

  if (providers.length === 0) return null;

  // Starts the provider's sign-in flow and shows a safe message if it fails.
  async function start(id: AuthProviderId) {
    setError(null);
    setPendingId(id);
    try {
      await signInWithProvider(id);
    } catch (e) {
      setError(authErrorMessage(e, "login"));
      setPendingId(null);
    }
  }

  return (
    <div className="mt-6 space-y-3">
      <p className="ts-label flex items-center gap-3 text-ink-dim before:h-px before:flex-1 before:bg-border-subtle after:h-px after:flex-1 after:bg-border-subtle">
        or
      </p>
      {providers.map((provider) => (
        <Button
          key={provider.id}
          variant="secondary"
          className="w-full"
          disabled={pendingId !== null}
          onClick={() => void start(provider.id)}
        >
          Continue with {provider.label}
        </Button>
      ))}
      <FormMessage message={error} />
    </div>
  );
}
