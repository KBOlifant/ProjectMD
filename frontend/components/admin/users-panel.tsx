"use client";

import { useCallback, useEffect, useState } from "react";
import { useAuth } from "@/app/functions/auth/useAuth";
import { adminErrorMessage, listAdminUsers, setUserRole, type AdminUser } from "@/app/functions/data/admin-api";
import type { Role } from "@/app/functions/rbac/roles";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/empty-state";
import { UsersTable } from "./users-table";

// What the list is doing: loading, failed (with a message to show) or loaded.
type View = { status: "loading" } | { status: "error"; message: string } | { status: "ready"; users: AdminUser[] };

// Returns a copy of the record without `key`. Stores nothing.
function without<T>(record: Record<string, T>, key: string): Record<string, T> {
  const next = { ...record };
  delete next[key];
  return next;
}

// Grey pulsing placeholder rows shown while the user list loads. Announced as busy. Stores nothing.
function UsersSkeleton() {
  return (
    <div role="status" aria-busy="true" className="animate-pulse space-y-3">
      <span className="sr-only">Loading users</span>
      <div aria-hidden="true" className="space-y-3 rounded-lg border border-border-subtle bg-bg-surface p-4">
        {[0, 1, 2, 3].map((i) => (
          <div key={i} className="h-9 rounded-md bg-bg-raised" />
        ))}
      </div>
    </div>
  );
}

// The admin Users list (api mode, admins only): loads GET /api/admin/users, shows a skeleton while
// loading, an error with Retry if that fails, an empty state, or the table. Changing a role calls
// PUT /api/admin/users/{id}/role: that row is disabled and shows "Saving…" until the server answers,
// then it is replaced by the updated user, or shows the mapped error message. A screen-reader-only
// live region announces each result. The signed-in admin's own row cannot be changed (found by
// email). The list, the pending roles and the row errors are temporary state; nothing is stored.
export function UsersPanel() {
  const { user: me } = useAuth();
  const [view, setView] = useState<View>({ status: "loading" });
  const [pending, setPending] = useState<Record<string, Role>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [announcement, setAnnouncement] = useState("");

  // Loads the user list and returns the resulting view (never rejects). Changes no state itself.
  const fetchView = useCallback(async (): Promise<View> => {
    try {
      return { status: "ready", users: await listAdminUsers() };
    } catch (err) {
      return { status: "error", message: adminErrorMessage(err, "Could not load users.") };
    }
  }, []);

  // Load the list once when the panel appears (ignored if it was removed before the answer came).
  useEffect(() => {
    let cancelled = false;
    void fetchView().then((next) => {
      if (!cancelled) setView(next);
    });
    return () => {
      cancelled = true;
    };
  }, [fetchView]);

  // "Retry" after a failed load: back to the skeleton, then load again.
  function retry() {
    setView({ status: "loading" });
    void fetchView().then(setView);
  }

  // True for the signed-in admin's own row. The list has database ids, not Firebase uids, so the
  // match is by email (case-insensitive).
  function isSelf(row: AdminUser): boolean {
    return me?.email != null && row.email.toLowerCase() === me.email.toLowerCase();
  }

  // Saves a new role for one user: marks the row as saving, calls the backend, then swaps in the
  // updated user or records the error message for that row, and announces the outcome.
  async function changeRole(row: AdminUser, role: Role) {
    if (role === row.role || pending[row.id] !== undefined) return;
    setPending((p) => ({ ...p, [row.id]: role }));
    setErrors((e) => without(e, row.id));
    try {
      const updated = await setUserRole(row.id, role);
      setView((v) =>
        v.status === "ready" ? { status: "ready", users: v.users.map((u) => (u.id === updated.id ? updated : u)) } : v,
      );
      setAnnouncement(`${updated.email} is now ${updated.role}.`);
    } catch (err) {
      const message = adminErrorMessage(err, "Could not change the role. Try again.");
      setErrors((e) => ({ ...e, [row.id]: message }));
      setAnnouncement(`${row.email}: ${message}`);
    } finally {
      setPending((p) => without(p, row.id));
    }
  }

  return (
    <div>
      <p role="status" aria-live="polite" className="sr-only">
        {announcement}
      </p>
      {view.status === "loading" ? (
        <UsersSkeleton />
      ) : view.status === "error" ? (
        <EmptyState action={<Button onClick={retry}>Retry</Button>}>
          <span role="alert" className="text-danger">
            {view.message}
          </span>
        </EmptyState>
      ) : view.users.length === 0 ? (
        <EmptyState>No users yet. People appear here after they sign in.</EmptyState>
      ) : (
        <>
          <p className="ts-small mb-4 text-ink-muted">
            {view.users.length} {view.users.length === 1 ? "user" : "users"}, newest first. Role changes apply
            right away.
          </p>
          <UsersTable users={view.users} pending={pending} errors={errors} isSelf={isSelf} onChange={changeRole} />
        </>
      )}
    </div>
  );
}
