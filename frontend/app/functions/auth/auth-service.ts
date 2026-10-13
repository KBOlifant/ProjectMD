import {
  createUserWithEmailAndPassword,
  reload,
  sendEmailVerification,
  sendPasswordResetEmail,
  signInWithEmailAndPassword,
  signOut,
} from "firebase/auth";
import { getFirebaseAuth } from "@/app/functions/firebase/client";
import { findProvider } from "./providers";

// Thin wrapper over Firebase Auth. UI code calls these functions and never imports "firebase/*".
// Nothing here logs or stores passwords or tokens; Firebase keeps the session in the browser.
//
// Everything checked on the client (signed in, verified, role) is UX only. The backend must verify
// the Firebase ID token from getIdToken() and re-check the role and `email_verified` on every API call.

// Creates the account, then sends the verification email. If the email fails to send, sign-up still
// succeeds and `verificationEmailSent` is false so the verify screen can offer a resend. Resolves
// with the outcome only; the new session is stored by Firebase in the browser.
export async function signUp(email: string, password: string): Promise<{ verificationEmailSent: boolean }> {
  const credential = await createUserWithEmailAndPassword(getFirebaseAuth(), email, password);
  try {
    await sendEmailVerification(credential.user);
    return { verificationEmailSent: true };
  } catch {
    return { verificationEmailSent: false };
  }
}

// Signs in with email and password. The session is stored by Firebase in the browser.
export async function signIn(email: string, password: string): Promise<void> {
  await signInWithEmailAndPassword(getFirebaseAuth(), email, password);
}

// Signs out the current user and clears the Firebase session from the browser.
export async function signOutUser(): Promise<void> {
  await signOut(getFirebaseAuth());
}

// Sends (or re-sends) the verification email to the signed-in user. Throws if nobody is signed in.
export async function sendVerificationEmail(): Promise<void> {
  const user = getFirebaseAuth().currentUser;
  if (!user) throw new Error("Not signed in.");
  await sendEmailVerification(user);
}

// Sends a password reset email. An unknown email is swallowed on purpose so the caller shows the
// same success text either way and nobody can probe which emails have accounts.
export async function sendPasswordReset(email: string): Promise<void> {
  try {
    await sendPasswordResetEmail(getFirebaseAuth(), email);
  } catch (error) {
    if (typeof error === "object" && error !== null && (error as { code?: unknown }).code === "auth/user-not-found") return;
    throw error;
  }
}

// Re-reads the signed-in user from Firebase so `emailVerified` is current. No-op when signed out.
// Does not refresh the ID token; AuthProvider.refresh() does both.
export async function reloadUser(): Promise<void> {
  const user = getFirebaseAuth().currentUser;
  if (user) await reload(user);
}

// Returns the current Firebase ID token to send as `Authorization: Bearer <token>` to the backend,
// or null when signed out. Pass true to force a fresh token (for example after the email was verified).
export async function getIdToken(forceRefresh = false): Promise<string | null> {
  const user = getFirebaseAuth().currentUser;
  return user ? user.getIdToken(forceRefresh) : null;
}

// Returns the signed-in user's uid, or null when signed out (or when called on the server).
export function getCurrentUserId(): string | null {
  if (typeof window === "undefined") return null;
  return getFirebaseAuth().currentUser?.uid ?? null;
}

// Signs in through a third-party provider from the registry. Throws for unknown or disabled ids.
// GitHub is registered but disabled, so today this always throws; enabling it means adding its case here.
export async function signInWithProvider(id: string): Promise<void> {
  const provider = findProvider(id);
  if (!provider || !provider.enabled) {
    throw new Error(`Sign-in provider "${id}" is not available.`);
  }
  // No third-party flow exists yet. When GitHub is built, branch on provider.id above this line.
  throw new Error(`Sign-in provider "${id}" has no sign-in flow yet. Use signIn() for email and password.`);
}
