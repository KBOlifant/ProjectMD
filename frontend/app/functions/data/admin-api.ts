import { isRole, type Role } from "@/app/functions/rbac/roles";
import { ApiError, apiFetch } from "./api-client";

// Calls the admin endpoints of the backend (api mode only): list users, change a user's role and delete a user.
// The backend checks the Firebase ID token and that the caller is an admin on every request; hiding
// the screen in the browser is only a convenience. Tokens and bodies are never logged or stored.

// One row of the admin Users table. `id` is the database id (not the Firebase uid). Dates are the
// ISO strings the backend sends; `lastSeenAt` is null when the user was never seen.
export interface AdminUser {
  id: string;
  email: string;
  role: Role;
  createdAt: string | null;
  lastSeenAt: string | null;
}

// Returns the value when it is a non-empty string, otherwise null.
function str(value: unknown): string | null {
  return typeof value === "string" && value !== "" ? value : null;
}

// Turns one raw item from the backend into an AdminUser, failing closed: an unknown role becomes
// "viewer". Returns null for an item without an id, so a broken row is skipped instead of crashing.
function toAdminUser(raw: unknown): AdminUser | null {
  if (typeof raw !== "object" || raw === null) return null;
  const f = raw as Record<string, unknown>;
  const id = str(f.id);
  if (id === null) return null;
  return {
    id,
    email: str(f.email) ?? "(no email)",
    role: isRole(f.role) ? f.role : "viewer",
    createdAt: str(f.createdAt),
    lastSeenAt: str(f.lastSeenAt),
  };
}

// GET /api/admin/users -> the users, newest first (the backend returns up to 200). Rejects with an
// ApiError; pass it to adminErrorMessage for text that is safe to show.
export async function listAdminUsers(): Promise<AdminUser[]> {
  const rows = await apiFetch<unknown>("/api/admin/users");
  if (!Array.isArray(rows)) return [];
  return rows.map(toAdminUser).filter((u): u is AdminUser => u !== null);
}

// PUT /api/admin/users/{id}/role { role } -> the updated user. Rejects with an ApiError (reason
// "role" for an unknown role, "self" for your own account, "last-admin" for the last admin).
export async function setUserRole(id: string, role: Role): Promise<AdminUser> {
  const raw = await apiFetch<unknown>(`/api/admin/users/${encodeURIComponent(id)}/role`, {
    method: "PUT",
    body: JSON.stringify({ role }),
  });
  const user = toAdminUser(raw);
  if (user === null) throw new ApiError(502, "The server sent an unexpected response.");
  return user;
}

// DELETE /api/admin/users/{id} -> resolves when the user (and their scans) are gone. Rejects with an
// ApiError (reason "self" for your own account, "last-admin" for the last admin). The user's Firebase
// account is not removed.
export async function deleteUser(id: string): Promise<void> {
  await apiFetch<void>(`/api/admin/users/${encodeURIComponent(id)}`, { method: "DELETE" });
}

// Turns any error from the admin calls into a short message that is safe to show: your own role
// ("self"), the last admin ("last-admin"), no access (403), unknown role or user, the server being
// unreachable, or `fallback` for anything else. `action` picks the wording for "self" (changing your
// role or deleting your account). Server text is never shown.
export function adminErrorMessage(err: unknown, fallback: string, action: "role" | "delete" = "role"): string {
  if (!(err instanceof ApiError)) return fallback;
  if (err.status === 0) return "Can't reach the server.";
  if (err.status === 401) return "Your session has expired. Sign in again.";
  if (err.status === 403) return "You don't have access.";
  if (err.status === 404) return "That user no longer exists.";
  if (err.reason === "self") return action === "delete" ? "You can't delete your own account." : "You can't change your own role.";
  if (err.reason === "last-admin") return "There must be at least one admin.";
  if (err.status === 400 && err.reason === "role") return "That isn't a valid role.";
  return fallback;
}
