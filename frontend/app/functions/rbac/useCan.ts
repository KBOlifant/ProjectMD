"use client";

import { useAuth } from "../auth/useAuth";
import { can, denyReason, type DenyReason } from "./can";
import type { Permission } from "./permissions";

// React hook: true when the current user may do `permission`. False while auth is still
// loading and when signed out, so gated UI never flashes open. Reads the user from useAuth();
// stores nothing.
export function useCan(permission: Permission): boolean {
  const { status, user } = useAuth();
  if (status === "loading") return false;
  return can(user, permission);
}

// React hook: why the current user may not do `permission`, or null. While auth is loading it
// also returns null, which does NOT mean allowed: gate on useCan() and use this only to pick
// the message to show. Reads the user from useAuth(); stores nothing.
export function useDenyReason(permission: Permission): DenyReason | null {
  const { status, user } = useAuth();
  if (status === "loading") return null;
  return denyReason(user, permission);
}
