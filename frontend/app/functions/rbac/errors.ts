import { denyReason, type DenyReason } from "./can";
import type { Permission } from "./permissions";
import type { CurrentUser } from "./types";

// Error thrown when an action is attempted without permission. Carries which permission was
// needed and why it was refused so callers can show a fitting message.
export class PermissionError extends Error {
  readonly permission: Permission;
  readonly reason: DenyReason;

  constructor(permission: Permission, reason: DenyReason) {
    super(permissionMessage(reason));
    this.name = "PermissionError";
    this.permission = permission;
    this.reason = reason;
  }
}

// Plain-English message for a refusal reason. Stores nothing.
function permissionMessage(reason: DenyReason): string {
  switch (reason) {
    case "signed-out":
      return "Sign in to continue.";
    case "unverified":
      return "Verify your email to continue.";
    case "role":
      return "Your account does not have permission to do this.";
  }
}

// Throws a PermissionError unless the user may do `permission`. After it returns, TypeScript
// knows the user is signed in. Stores nothing. This is a client-side guard only; the backend
// must still check the ID token.
export function assertCan(
  user: CurrentUser | null,
  permission: Permission,
): asserts user is CurrentUser {
  const reason = denyReason(user, permission);
  if (reason) throw new PermissionError(permission, reason);
}
