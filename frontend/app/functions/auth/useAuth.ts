"use client";

import { useContext } from "react";
import { AuthContext, type AuthContextValue } from "./AuthProvider";

// Hook for components to read the auth state: { status: "loading" | "authed" | "anon", user, refresh }.
// `user` is a CurrentUser (uid, email, emailVerified, role) or null. Throws if used outside
// AuthProvider. Stores nothing.
export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside AuthProvider");
  return ctx;
}
