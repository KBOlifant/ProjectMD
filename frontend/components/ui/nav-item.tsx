import Link from "next/link";
import type { ReactNode } from "react";

interface NavItemProps {
  href: string;
  label: string;
  icon: ReactNode;
  active?: boolean;
  count?: number;
  // "rail" = icon-only tile for the desktop rail (label becomes the accessible name and a hover
  // tooltip). "row" = icon + text for the mobile top bar.
  layout?: "rail" | "row";
}

// A single navigation link with icon, active state and optional count badge. Stores nothing.
export function NavItem({ href, label, icon, active = false, count, layout = "rail" }: NavItemProps) {
  const hasCount = count !== undefined && count > 0;

  if (layout === "row") {
    return (
      <Link
        href={href}
        aria-current={active ? "page" : undefined}
        className={`ts-mono-ui flex items-center gap-2 whitespace-nowrap rounded-md border px-3 py-2 transition-colors ${
          active
            ? "border-border-strong bg-bg-raised text-ink"
            : "border-transparent text-ink-muted hover:bg-bg-surface hover:text-ink"
        }`}
      >
        {icon}
        <span>{label}</span>
        {hasCount && <span className="rounded-sm bg-plum-500 px-1.5 font-mono text-[10px] text-on-primary">{count}</span>}
      </Link>
    );
  }

  return (
    <Link
      href={href}
      aria-current={active ? "page" : undefined}
      aria-label={hasCount ? `${label}, ${count} saved` : label}
      className={`group relative grid size-11 place-items-center rounded-lg border transition-colors ${
        active
          ? "border-border-strong bg-bg-raised text-ink"
          : "border-transparent text-ink-muted hover:bg-bg-surface hover:text-ink"
      }`}
    >
      {active && (
        <span aria-hidden="true" className="absolute -left-3.5 top-1/2 h-6 w-[3px] -translate-y-1/2 rounded-r-sm bg-violet-500" />
      )}
      {icon}
      {hasCount && (
        <span
          aria-hidden="true"
          className="absolute -right-1 -top-1 grid h-4 min-w-4 place-items-center rounded-full bg-plum-500 px-1 font-mono text-[9px] leading-none text-on-primary"
        >
          {count}
        </span>
      )}
      <span
        aria-hidden="true"
        className="ts-mono-ui pointer-events-none absolute left-full z-30 ml-3 whitespace-nowrap rounded-md border border-border-strong bg-bg-raised px-2 py-1 text-ink opacity-0 transition-opacity group-hover:opacity-100 group-focus-visible:opacity-100"
      >
        {label}
      </span>
    </Link>
  );
}
