import Link from "next/link";
import type { ReactNode } from "react";
import { VerifyBanner } from "@/components/auth-status/verify-banner";
import { UserMenu, UserMenuCompact } from "@/components/auth-status/user-menu";
import { MainNav } from "./main-nav";
import { ServerNotice } from "./server-notice";
import { UsageMeter } from "./usage-meter";

// The "Project.MD" logo link back to the home page. Stores nothing.
function Brand() {
  return (
    <Link href="/" className="ts-h3 text-ink">
      Project<span className="text-rose-400">.MD</span>
    </Link>
  );
}

// Page frame: desktop sidebar (logo, nav, user menu, usage meter), mobile top bar (logo, compact
// user menu, nav), and the content area with the "can't reach the server" notice (server mode only)
// and the verify-email banner above the page. The nav shows an Admin link to admins only. Stores nothing.
export function Shell({ children }: { children: ReactNode }) {
  return (
    <div className="min-h-screen md:flex">
      <aside className="hidden w-60 flex-none flex-col border-r border-border-subtle bg-bg-sidebar p-4 md:sticky md:top-0 md:flex md:h-screen">
        <div className="px-3 py-2">
          <Brand />
        </div>
        <p className="ts-label mb-2 mt-6 px-3 text-ink-dim">Workspace</p>
        <MainNav />
        <div className="mt-auto space-y-3">
          <UserMenu />
          <UsageMeter />
        </div>
      </aside>

      <header className="border-b border-border-subtle bg-bg-sidebar px-4 pt-3 md:hidden">
        <div className="flex items-center justify-between gap-3">
          <Brand />
          <UserMenuCompact />
        </div>
        <MainNav row />
      </header>

      <main className="min-w-0 flex-1">
        <ServerNotice />
        <VerifyBanner />
        <div className="mx-auto w-full max-w-[1040px] px-4 py-8 md:px-8">{children}</div>
      </main>
    </div>
  );
}
