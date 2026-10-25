import Link from "next/link";
import type { ReactNode } from "react";

interface NavItemProps {
  href: string;
  label: string;
  icon: ReactNode;
  active?: boolean;
  count?: number;
}

// A single sidebar/nav link with icon, active highlight and optional count badge. Stores nothing.
export function NavItem({ href, label, icon, active = false, count }: NavItemProps) {
  return (
    <Link
      href={href}
      aria-current={active ? "page" : undefined}
      className={`relative flex items-center gap-3 whitespace-nowrap rounded-md px-3 py-2.5 ts-mono-ui transition-colors ${
        active ? "bg-fig-500/30 text-ink" : "text-ink-muted hover:bg-bg-raised hover:text-ink"
      }`}
    >
      {active && <span aria-hidden="true" className="absolute inset-y-2 left-0 w-0.5 rounded-sm bg-mulberry-500" />}
      <span className={active ? "text-ink" : "text-ink-dim"}>{icon}</span>
      <span className={active ? "font-medium" : ""}>{label}</span>
      {count !== undefined && count > 0 && (
        <span className="ts-small ml-auto rounded-sm bg-bg-raised px-1.5 font-mono text-ink-muted">{count}</span>
      )}
    </Link>
  );
}
