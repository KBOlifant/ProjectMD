// Pure permission logic: no React, no Firebase, so it can be tested with a plain script.
import { ROLE_PERMISSIONS, type Permission } from "./permissions";
import { isRole } from "./roles";
import type { CurrentUser } from "./types";

// Why a permission was refused: nobody is signed in, the email is not verified, or the role
// does not include the permission.
export type DenyReason = "signed-out" | "unverified" | "role";

// Returns why the user may not do `permission`, or null when they may. Fails closed: a missing
// user, an unknown role or an unknown permission is refused. The role is checked before the
// email so a viewer is told "role" (verifying would not help). "scan:create" additionally
// requires emailVerified to be exactly true for every role. Stores nothing.
export function denyReason(user: CurrentUser | null, permission: Permission): DenyReason | null {
  if (!user) return "signed-out";
  if (!isRole(user.role) || !ROLE_PERMISSIONS[user.role].includes(permission)) return "role";
  if (permission === "scan:create" && user.emailVerified !== true) return "unverified";
  return null;
}

// True when the user is allowed to do `permission`. Stores nothing.
export function can(user: CurrentUser | null, permission: Permission): boolean {
  return denyReason(user, permission) === null;
}
