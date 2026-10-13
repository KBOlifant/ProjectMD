// This matrix is a UX convenience and a first line of defence only. It hides or disables
// controls and stops obvious misuse in the browser, but the browser cannot be trusted: the
// backend must enforce the same matrix (and the verified-email rule for "scan:create") from the
// verified Firebase ID token on every request.
import type { Role } from "./roles";

export type Permission =
  | "scan:create"
  | "scan:read"
  | "scan:export"
  | "history:clear"
  | "admin:access";

// What each role may do. Spelled out in full (no inheritance) so it is easy to audit.
// The extra rule that "scan:create" also needs a verified email lives in can.ts.
export const ROLE_PERMISSIONS: Record<Role, readonly Permission[]> = {
  viewer: ["scan:read", "scan:export"],
  member: ["scan:read", "scan:export", "scan:create", "history:clear"],
  admin: ["scan:read", "scan:export", "scan:create", "history:clear", "admin:access"],
};
