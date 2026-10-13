// Turns Firebase auth errors into short, safe messages for the screens. Never returns raw Firebase
// text, and never logs the error (it can carry an email address).

export type AuthErrorContext = "login" | "signup" | "reset" | "verify";

// Shown after a password reset request whether or not the account exists, so the screen never
// reveals which emails are registered.
export const RESET_SUCCESS_MESSAGE = "If an account exists for that email, a reset link is on its way.";

// Pulls the Firebase error code (like "auth/invalid-credential") out of an unknown thrown value.
// Returns "" when there is none. Stores nothing.
function errorCode(error: unknown): string {
  if (typeof error === "object" && error !== null && "code" in error) {
    const code = (error as { code: unknown }).code;
    if (typeof code === "string") return code;
  }
  return "";
}

// Maps a thrown auth error to a user-facing message for the given screen. Login and reset never
// reveal whether an email exists; only signup says an email is already taken (it has to). For the
// reset screen an unknown account maps to the same text as success. Stores nothing.
export function authErrorMessage(error: unknown, context: AuthErrorContext): string {
  const code = errorCode(error);
  switch (code) {
    case "auth/invalid-credential":
    case "auth/user-not-found":
    case "auth/wrong-password":
      return context === "reset" ? RESET_SUCCESS_MESSAGE : "Incorrect email or password.";
    case "auth/email-already-in-use":
      return context === "signup"
        ? "An account with this email already exists. Sign in instead."
        : "Something went wrong. Try again.";
    case "auth/weak-password":
      return "That password is too weak. Choose a longer, harder-to-guess one.";
    case "auth/invalid-email":
      return "Enter a valid email address.";
    case "auth/too-many-requests":
      return "Too many attempts. Try again later.";
    case "auth/network-request-failed":
      return "Check your connection and try again.";
    default:
      return "Something went wrong. Try again.";
  }
}
