import Image from "next/image";
import Link from "next/link";
import type { ReactNode } from "react";
import smallLogo from "@/app/assets/SmallLogo.png";
import { VerifyBanner } from "@/components/auth-status/verify-banner";
import { UserMenuCompact } from "@/components/auth-status/user-menu";
import { MainNav } from "./main-nav";
import { ServerNotice } from "./server-notice";
import { UsageMeter } from "./usage-meter";

// The "MD" tile that links back to the home page. Stores nothing.
function BrandTile() {
  return (
    <Link
      href="/"
      aria-label="Project MD home"
      className="grid size-[38px] flex-none place-items-center rounded-lg border border-violet-500/50 bg-bg-base"
    >
      <Image src={smallLogo} alt="" className="h-auto w-[26px]" />
    </Link>
  );
}

// Page frame: a 72px icon rail on desktop (logo tile, nav, account avatar, vertical usage meter), a
// top bar on mobile (logo, account, nav row), and the content area to the right with the "can't reach
// the server" notice (server mode only) and the verify-email banner above the page. The pages draw
// their own width and padding, because Generate and Results run edge to edge while History and
// Admin sit in a reading column (see PageFrame). Stores nothing.
export function Shell({ children }: { children: ReactNode }) {
  return (
    <div className="min-h-screen md:pl-18">
      <aside className="fixed inset-y-0 left-0 z-20 hidden w-18 flex-col items-center border-r border-border-subtle bg-bg-sidebar py-[18px] md:flex">
        <BrandTile />
        <div className="mt-10">
          <MainNav />
        </div>
        <div className="mt-auto flex flex-col items-center gap-3">
          <UserMenuCompact placement="rail" />
          <UsageMeter />
        </div>
      </aside>

      <header className="border-b border-border-subtle bg-bg-sidebar px-4 pt-3 md:hidden">
        <div className="flex items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <BrandTile />
            <span className="ts-h3 text-ink">Project MD</span>
          </div>
          <UserMenuCompact placement="top-bar" />
        </div>
        <MainNav row />
      </header>

      <main className="min-w-0">
        <ServerNotice />
        <VerifyBanner />
        {children}
      </main>
    </div>
  );
}
