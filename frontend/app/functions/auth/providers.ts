// Registry of sign-in methods. This is the only place that changes when a provider is added.
//
// To add GitHub later: set `enabled: true` below, then in auth-service.ts add a "github" case to
// signInWithProvider that uses GithubAuthProvider + signInWithPopup, and handle
// `auth/account-exists-with-different-credential` there. The login screen renders its buttons
// from getEnabledSocialProviders(), so it needs no change.

export type AuthProviderId = "password" | "github";

export interface AuthProviderInfo {
  id: AuthProviderId;
  label: string;
  enabled: boolean;
}

export const AUTH_PROVIDERS: readonly AuthProviderInfo[] = [
  { id: "password", label: "Email", enabled: true },
  { id: "github", label: "GitHub", enabled: false },
];

// Looks up a provider by id. Returns undefined for unknown ids. Stores nothing.
export function findProvider(id: string): AuthProviderInfo | undefined {
  return AUTH_PROVIDERS.find((p) => p.id === id);
}

// The enabled providers that get their own button (everything except email/password).
// Empty today. Stores nothing.
export function getEnabledSocialProviders(): AuthProviderInfo[] {
  return AUTH_PROVIDERS.filter((p) => p.enabled && p.id !== "password");
}
