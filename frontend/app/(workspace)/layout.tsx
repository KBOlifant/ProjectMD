import { RequireAuth } from "@/app/functions/auth/guards";
import { Shell } from "@/components/shell/shell";

// Layout for the signed-in app (/, /history, /processing, /results): only signed-in users get past
// RequireAuth, then the sidebar Shell wraps the page. The URL is unchanged by the (workspace)
// folder. Stores nothing.
export default function WorkspaceLayout({ children }: LayoutProps<"/">) {
  return (
    <RequireAuth>
      <Shell>{children}</Shell>
    </RequireAuth>
  );
}
