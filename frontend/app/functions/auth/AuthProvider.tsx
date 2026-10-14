"use client";

import { onAuthStateChanged, type User } from "firebase/auth";
import { createContext, useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { reloadMe, setMeOwner, useMeState } from "@/app/functions/data/me-store";
import { isApiMode } from "@/app/functions/data/mode";
import { getFirebaseAuth } from "@/app/functions/firebase/client";
import { resolveRole, type Role } from "@/app/functions/rbac/roles";
import type { CurrentUser } from "@/app/functions/rbac/types";
import { setHistoryOwner } from "@/lib/history-store";
import { getIdToken, reloadUser } from "./auth-service";

export type AuthStatus = "loading" | "authed" | "anon";

export interface AuthContextValue {
  status: AuthStatus;
  user: CurrentUser | null;
  // Server mode only: true when the signed-in user's role could not be loaded from GET /api/me.
  // The user is then signed in with the safest role ("viewer"). Always false in static mode.
  error: boolean;
  refresh: () => Promise<void>;
}

interface AuthState {
  status: AuthStatus;
  user: CurrentUser | null;
}

export const AuthContext = createContext<AuthContextValue | null>(null);

// Static mode: builds the app's CurrentUser from a Firebase user: uid, email, verified flag, and the
// role read from the `role` custom claim on the ID token (resolveRole fails closed). If the token
// cannot be read, the user gets the least privileged role instead of an error. This is UX only; the
// backend must verify the ID token, role and `email_verified` itself on every API call. Stores nothing.
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

// Server mode: builds the identity part of the CurrentUser (uid, email, verified flag) from a
// Firebase user with the safest role. The real role is added in AuthProvider once GET /api/me has
// answered; the token claim and NEXT_PUBLIC_DEV_ROLE are ignored on purpose. Stores nothing.
function identityOf(fbUser: User): CurrentUser {
  return { uid: fbUser.uid, email: fbUser.email, emailVerified: fbUser.emailVerified, role: "viewer" };
}

// Holds the signed-in user for the whole app. It listens to Firebase for sign-in/sign-out, builds a
// CurrentUser (with its role) on every change, and tells the history store whose scans to show so
// each account keeps its own history. Starts as "loading" until Firebase has answered once. Where
// the role comes from depends on the mode: static mode reads the token claim (or the dev override);
// server mode (NEXT_PUBLIC_SCAN_PROVIDER=api) takes the role ONLY from GET /api/me (the database
// role), stays "loading" until that has answered, and falls back to "viewer" plus `error: true` if
// it fails, so a failure never grants more access. The state lives in memory only; Firebase itself
// keeps the session in the browser.
export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: "loading", user: null });
  const me = useMeState();

  // Subscribe once to Firebase auth changes. A counter drops stale results if changes overlap.
  useEffect(() => {
    let latest = 0;
    const unsubscribe = onAuthStateChanged(getFirebaseAuth(), (fbUser) => {
      const run = ++latest;
      if (!fbUser) {
        setMeOwner(null);
        setHistoryOwner(null);
        setState({ status: "anon", user: null });
        return;
      }
      if (isApiMode) {
        // Server mode: start the one GET /api/me for this user right away; the role is merged in
        // below once it has answered.
        setMeOwner(fbUser.uid);
        setHistoryOwner(fbUser.uid);
        setState({ status: "authed", user: identityOf(fbUser) });
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
  // changed role show up) and rebuilds the CurrentUser. In server mode it also asks GET /api/me
  // again, so a role change made by an admin appears after a refresh. Does nothing when signed out.
  const refresh = useCallback(async () => {
    await reloadUser();
    const fbUser = getFirebaseAuth().currentUser;
    if (!fbUser) return;
    await getIdToken(true);
    if (isApiMode) {
      setState({ status: "authed", user: identityOf(fbUser) });
      await reloadMe();
      return;
    }
    const user = await buildCurrentUser(fbUser);
    setHistoryOwner(user.uid);
    setState({ status: "authed", user });
  }, []);

  // Server mode: the role to use for the signed-in user, or null while GET /api/me has not answered
  // for them yet. A failed answer (or anything but a valid role) gives "viewer".
  const serverRole: Role | null = useMemo(() => {
    if (!isApiMode || state.status !== "authed" || state.user === null) return null;
    if (me.owner !== state.user.uid || !me.loaded) return null;
    return !me.error && me.me !== null ? me.me.role : "viewer";
  }, [me, state]);

  // The user the app sees. Static mode: exactly what was built above. Server mode: the Firebase
  // identity with the role from GET /api/me, or no user at all while that is still loading.
  const user = useMemo<CurrentUser | null>(() => {
    if (!isApiMode) return state.user;
    if (state.user === null || serverRole === null) return null;
    return { ...state.user, role: serverRole };
  }, [state.user, serverRole]);

  const status: AuthStatus = isApiMode && state.status === "authed" && serverRole === null ? "loading" : state.status;
  const error = isApiMode && state.status === "authed" && serverRole !== null && me.error;

  const value = useMemo<AuthContextValue>(
    () => ({ status, user, error, refresh }),
    [status, user, error, refresh],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
