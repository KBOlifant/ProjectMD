import Link from "next/link";
import type { ReactNode } from "react";
import { MainNav } from "./main-nav";
import { UsageMeter } from "./usage-meter";

// The "Project.MD" logo link back to the home page. Stores nothing.
function Brand() {
  return (
    <Link href="/" className="ts-h3 text-ink">
      Project<span className="text-violet-400">.MD</span>
    </Link>
  );
}

// Page frame: desktop sidebar (logo, nav, usage meter), mobile top bar, and the content area. Stores nothing.
export function Shell({ children }: { children: ReactNode }) {
  return (
    <div className="min-h-screen md:flex">
      <aside className="hidden w-60 flex-none flex-col border-r border-border-subtle bg-bg-sidebar p-4 md:sticky md:top-0 md:flex md:h-screen">
        <div className="px-3 py-2">
          <Brand />
        </div>
        <p className="ts-label mb-2 mt-6 px-3 text-ink-dim">Workspace</p>
        <MainNav />
        <div className="mt-auto">
          <UsageMeter />
        </div>
      </aside>

      <header className="border-b border-border-subtle bg-bg-sidebar px-4 pt-3 md:hidden">
        <Brand />
        <MainNav row />
      </header>

      <main className="min-w-0 flex-1">
        <div className="mx-auto w-full max-w-[1040px] px-4 py-8 md:px-8">{children}</div>
      </main>
    </div>
  );
}
