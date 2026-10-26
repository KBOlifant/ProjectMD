import { isApiMode } from "@/app/functions/data/mode";
import { Can } from "@/app/functions/rbac/can-gate";
import { UsersPanel } from "@/components/admin/users-panel";
import { Eyebrow, PageFrame } from "@/components/shell/page-frame";
import { EmptyState } from "@/components/ui/empty-state";

// /admin/users page: lets an admin see everyone who has signed in and change their role. Wrapped in
// the "admin:access" check, so everyone else sees "You don't have access to this page." (a
// convenience only: the backend refuses non-admins itself). Static mode has no backend, so admins
// there are told the screen needs the server. Stores nothing.
export default function AdminUsersPage() {
  return (
    <PageFrame>
      <Can permission="admin:access" fallback={<EmptyState>You don&apos;t have access to this page.</EmptyState>}>
        <Eyebrow>Admin</Eyebrow>
        <h1 className="ts-h1 mb-6 mt-2 text-ink">Users</h1>
        {isApiMode ? (
          <UsersPanel />
        ) : (
          <EmptyState>Managing users needs the server (set NEXT_PUBLIC_SCAN_PROVIDER=api).</EmptyState>
        )}
      </Can>
    </PageFrame>
  );
}
