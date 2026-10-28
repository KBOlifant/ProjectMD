import { useId, useState } from "react";
import type { AdminUser } from "@/app/functions/data/admin-api";
import { ROLES, type Role } from "@/app/functions/rbac/roles";
import { Button } from "@/components/ui/button";
import { DeleteButton } from "@/components/ui/delete-button";
import { TechChip } from "@/components/ui/tech-chip";
import { formatDate } from "@/lib/format";

// Formats an ISO date for the "Joined" column ("Oct 6, 2026"), or a dash when it is missing or not
// a valid date. Stores nothing.
function joined(iso: string | null): string {
  if (iso === null || Number.isNaN(new Date(iso).getTime())) return "–";
  return formatDate(iso);
}

// Formats an ISO date and time for the "Last seen" column ("Oct 6, 2026, 3:04 PM"), "Never" when
// the user was never seen, or a dash when the value is not a valid date. Stores nothing.
function lastSeen(iso: string | null): string {
  if (iso === null) return "Never";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "–";
  return date.toLocaleString("en-US", { dateStyle: "medium", timeStyle: "short" });
}

// One table row: the user's email, a labelled role <select>, last seen and joined. `pendingRole` is
// the role being saved (the select shows it and is disabled until the server answers), `message` is
// the last error for this row, and `isSelf` marks the signed-in admin's own row, whose select is
// disabled with an explanation (the backend refuses self-changes too). The Delete button asks for a
// second click ("Confirm" / "Cancel") before calling `onDelete`; `deleting` is true while that runs.
// Calls `onChange` with the new role; the only state kept is whether the confirmation is open.
function UserRow({
  user,
  isSelf,
  pendingRole,
  deleting,
  message,
  onChange,
  onDelete,
}: {
  user: AdminUser;
  isSelf: boolean;
  pendingRole: Role | undefined;
  deleting: boolean;
  message: string | undefined;
  onChange: (user: AdminUser, role: Role) => void;
  onDelete: (user: AdminUser) => void;
}) {
  const noteId = useId();
  const [confirming, setConfirming] = useState(false);
  const saving = pendingRole !== undefined;
  const note = deleting ? "Deleting…" : saving ? "Saving…" : message ? message : isSelf ? "You can't change your own role." : null;

  return (
    <tr className="border-b border-border-subtle last:border-b-0 align-top">
      <td className="px-4 py-3">
        <span className="ts-mono-ui flex items-center gap-2 text-ink">
          <span className="max-w-64 truncate" title={user.email}>
            {user.email}
          </span>
          {isSelf && <TechChip name="You" highlighted />}
        </span>
      </td>
      <td className="px-4 py-3">
        <select
          aria-label={`Role for ${user.email}`}
          aria-describedby={note ? noteId : undefined}
          aria-busy={saving}
          value={pendingRole ?? user.role}
          disabled={isSelf || saving || deleting}
          onChange={(e) => onChange(user, e.target.value as Role)}
          className="ts-mono-ui rounded-md border border-border-strong bg-bg-surface px-3 py-2 text-ink disabled:border-border-subtle disabled:text-ink-dim"
        >
          {ROLES.map((role) => (
            <option key={role} value={role} className="bg-bg-surface text-ink">
              {role}
            </option>
          ))}
        </select>
        <p id={noteId} className={`ts-small mt-1 ${message && !saving ? "text-danger" : "text-ink-muted"}`}>
          {note}
        </p>
      </td>
      <td className="ts-small whitespace-nowrap px-4 py-3 text-ink-muted">{lastSeen(user.lastSeenAt)}</td>
      <td className="ts-small whitespace-nowrap px-4 py-3 text-ink-muted">{joined(user.createdAt)}</td>
      <td className="whitespace-nowrap px-4 py-3">
        {confirming ? (
          <span className="flex gap-2">
            <Button
              variant="danger"
              aria-label={`Confirm delete ${user.email}`}
              disabled={deleting}
              onClick={() => {
                setConfirming(false);
                onDelete(user);
              }}
            >
              Confirm
            </Button>
            <Button variant="ghost" onClick={() => setConfirming(false)}>
              Cancel
            </Button>
          </span>
        ) : (
          <DeleteButton
            label={user.email}
            disabled={isSelf || saving || deleting}
            title={isSelf ? "You can't delete your own account." : undefined}
            onClick={() => setConfirming(true)}
          />
        )}
      </td>
    </tr>
  );
}

// The Users table. `pending` maps a user id to the role being saved, `deleting` marks users being
// deleted, `errors` maps a user id to the
// message from the last failed change, and `isSelf` says which row is the signed-in admin. It
// scrolls sideways on narrow screens. Display only; the parent does the saving and stores the state.
export function UsersTable({
  users,
  pending,
  deleting,
  errors,
  isSelf,
  onChange,
  onDelete,
}: {
  users: AdminUser[];
  pending: Record<string, Role>;
  deleting: Record<string, true>;
  errors: Record<string, string>;
  isSelf: (user: AdminUser) => boolean;
  onChange: (user: AdminUser, role: Role) => void;
  onDelete: (user: AdminUser) => void;
}) {
  const head = "ts-label px-4 py-3 text-left font-medium text-ink-dim";
  return (
    <div className="overflow-x-auto rounded-lg border border-border-subtle bg-bg-surface">
      <table className="w-full min-w-[640px] border-collapse">
        <caption className="sr-only">Users, their roles and a delete button</caption>
        <thead>
          <tr className="border-b border-border-subtle">
            <th scope="col" className={head}>
              Email
            </th>
            <th scope="col" className={head}>
              Role
            </th>
            <th scope="col" className={head}>
              Last seen
            </th>
            <th scope="col" className={head}>
              Joined
            </th>
            <th scope="col" className={head}>
              <span className="sr-only">Delete</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {users.map((user) => (
            <UserRow
              key={user.id}
              user={user}
              isSelf={isSelf(user)}
              pendingRole={pending[user.id]}
              deleting={deleting[user.id] === true}
              message={errors[user.id]}
              onChange={onChange}
              onDelete={onDelete}
            />
          ))}
        </tbody>
      </table>
    </div>
  );
}
