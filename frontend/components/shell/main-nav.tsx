"use client";

import { usePathname } from "next/navigation";
import { useCan } from "@/app/functions/rbac/useCan";
import { useHistory } from "@/lib/history-store";
import { BoltIcon, ClockIcon, FileIcon, LoaderIcon } from "@/components/ui/icons";
import { NavItem } from "@/components/ui/nav-item";

// Small "people" icon for the Admin link, drawn like the shared 16x16 icons. Stores nothing.
function UsersIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 16 16"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <circle cx="6" cy="5.5" r="2.5" />
      <path d="M1.5 14c0-2.5 2-4 4.5-4s4.5 1.5 4.5 4M10.5 3.2a2.5 2.5 0 0 1 0 4.6M12 10.3c1.5.5 2.5 1.8 2.5 3.7" />
    </svg>
  );
}

const ITEMS = [
  { href: "/", label: "Generate", icon: <BoltIcon /> },
  { href: "/processing", label: "Processing", icon: <LoaderIcon /> },
  { href: "/results", label: "Results", icon: <FileIcon /> },
  { href: "/history", label: "History", icon: <ClockIcon /> },
];

// Only shown to users who may "admin:access" (the role comes from the server in api mode).
const ADMIN_ITEM = { href: "/admin/users", label: "Admin", icon: <UsersIcon /> };

// Main navigation (Generate, Processing, Results, History, plus Admin for admins). Highlights the
// current route from the URL and shows the saved-scan count on History. `row` lays it out
// horizontally for mobile. The Admin link is hidden for everyone else; that is a convenience, the
// backend enforces the permission. Stores nothing.
export function MainNav({ row = false }: { row?: boolean }) {
  const pathname = usePathname();
  const history = useHistory();
  const canAdmin = useCan("admin:access");
  const items = canAdmin ? [...ITEMS, ADMIN_ITEM] : ITEMS;

  return (
    <nav
      aria-label="Main"
      className={row ? "-mx-2 flex gap-1 overflow-x-auto py-2" : "flex flex-col gap-1"}
    >
      {items.map((item) => {
        const active = item.href === "/" ? pathname === "/" : pathname.startsWith(item.href);
        return (
          <NavItem
            key={item.href}
            {...item}
            active={active}
            count={item.href === "/history" ? history.length : undefined}
          />
        );
      })}
    </nav>
  );
}
