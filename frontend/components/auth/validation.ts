// Client-side form checks for the auth screens. They are UX only (Firebase and the backend still
// validate). Each function returns a short error message, or null when the value is fine.

export const MIN_PASSWORD_LENGTH = 8;

// Checks an email address: not empty and shaped like name@host.tld. Stores nothing.
export function emailError(value: string): string | null {
  const email = value.trim();
  if (email === "") return "Enter your email address.";
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) return "Enter a valid email address.";
  return null;
}

// Checks the password typed on the sign-in screen: only that something was entered. Stores nothing.
export function loginPasswordError(value: string): string | null {
  return value === "" ? "Enter your password." : null;
}

// Checks a new password for sign-up: not empty and at least MIN_PASSWORD_LENGTH characters. Stores nothing.
export function newPasswordError(value: string): string | null {
  if (value === "") return "Choose a password.";
  if (value.length < MIN_PASSWORD_LENGTH) return `Use at least ${MIN_PASSWORD_LENGTH} characters.`;
  return null;
}

// Checks the "confirm password" box against the password. Stores nothing.
export function confirmPasswordError(password: string, confirm: string): string | null {
  if (confirm === "") return "Confirm your password.";
  if (confirm !== password) return "Passwords do not match.";
  return null;
}
