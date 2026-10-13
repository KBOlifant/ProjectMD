"use client";

import { onAuthStateChanged, type User } from "firebase/auth";
import { createContext, useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { getFirebaseAuth } from "@/app/functions/firebase/client";
import { resolveRole } from "@/app/functions/rbac/roles";
import type { CurrentUser } from "@/app/functions/rbac/types";
import { setHistoryOwner } from "@/lib/history-store";
import { getIdToken, reloadUser } from "./auth-service";

export type AuthStatus = "loading" | "authed" | "anon";

export interface AuthContextValue {
  status: AuthStatus;
  user: CurrentUser | null;
  refresh: () => Promise<void>;
}

interface AuthState {
  status: AuthStatus;
  user: CurrentUser | null;
}

export const AuthContext = createContext<AuthContextValue | null>(null);

// Builds the app's CurrentUser from a Firebase user: uid, email, verified flag, and the role read
// from the `role` custom claim on the ID token (resolveRole fails closed). If the token cannot be
// read, the user gets the least privileged role instead of an error. This is UX only; the backend
// must verify the ID token, role and `email_verified` itself on every API call. Stores nothing.
async function buildCurrentUser(fbUser: User): Promise<CurrentUser> {
  let role = resolveRole("viewer");
  try {
    const result = await fbUser.getIdTokenResult();
    role = resolveRole(result.claims.role);
  } catch {
    // token unreadable (offline, expired session): keep the fail-closed role
  }
  return { uid: fbUser.uid, email: fbUser.email, emailVerified: fbUser.emailVerified, role };
}

// Holds the signed-in user for the whole app. It listens to Firebase for sign-in/sign-out, builds a
// CurrentUser (with its role) on every change, and tells the history store whose scans to show so
// each account keeps its own history. Starts as "loading" until Firebase has answered once. The
// state lives in memory only; Firebase itself keeps the session in the browser.
export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: "loading", user: null });

  // Subscribe once to Firebase auth changes. A counter drops stale results if changes overlap.
  useEffect(() => {
    let latest = 0;
    const unsubscribe = onAuthStateChanged(getFirebaseAuth(), (fbUser) => {
      const run = ++latest;
      if (!fbUser) {
        setHistoryOwner(null);
        setState({ status: "anon", user: null });
        return;
      }
      // A user appeared (fresh sign-in or restored session): hold on "loading" until the role is
      // read, so guards never see a half-built user or bounce a user who just signed in.
      setState((prev) => (prev.status === "authed" ? prev : { status: "loading", user: null }));
      void buildCurrentUser(fbUser).then((user) => {
        if (run !== latest) return;
        setHistoryOwner(user.uid);
        setState({ status: "authed", user });
      });
    });
    return unsubscribe;
  }, []);

  // Re-reads the user from Firebase, forces a fresh ID token (so a newly verified email and any
  // changed role claim show up) and rebuilds the CurrentUser. Does nothing when signed out.
  const refresh = useCallback(async () => {
    await reloadUser();
    const fbUser = getFirebaseAuth().currentUser;
    if (!fbUser) return;
    await getIdToken(true);
    const user = await buildCurrentUser(fbUser);
    setHistoryOwner(user.uid);
    setState({ status: "authed", user });
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({ status: state.status, user: state.user, refresh }),
    [state.status, state.user, refresh],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
