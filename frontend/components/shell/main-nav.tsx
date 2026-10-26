"use client";

import { usePathname } from "next/navigation";
import { useCan } from "@/app/functions/rbac/useCan";
import { useHistory } from "@/lib/history-store";
import { FileIcon, HistoryIcon, ScanIcon, SparkIcon, UsersIcon } from "@/components/ui/icons";
import { NavItem } from "@/components/ui/nav-item";

const ITEMS = [
  { href: "/", label: "Generate", icon: <SparkIcon size={18} /> },
  { href: "/processing", label: "Processing", icon: <ScanIcon size={18} /> },
  { href: "/results", label: "Results", icon: <FileIcon size={18} /> },
  { href: "/history", label: "History", icon: <HistoryIcon size={18} /> },
];

// Only shown to users who may "admin:access" (the role comes from the server in api mode).
const ADMIN_ITEM = { href: "/admin/users", label: "Admin", icon: <UsersIcon size={18} /> };

// Main navigation (Generate, Processing, Results, History, plus Admin for admins). Highlights the
// current route from the URL and shows the saved-scan count on History. Default is the vertical
// icon rail; `row` lays icon + text out horizontally for the mobile top bar. The Admin link is
// hidden for everyone else; that is a convenience, the backend enforces the permission. Stores nothing.
export function MainNav({ row = false }: { row?: boolean }) {
  const pathname = usePathname();
  const history = useHistory();
  const canAdmin = useCan("admin:access");
  const items = canAdmin ? [...ITEMS, ADMIN_ITEM] : ITEMS;

  return (
    <nav
      aria-label="Main"
      className={row ? "-mx-2 flex gap-1 overflow-x-auto py-2" : "flex flex-col gap-2"}
    >
      {items.map((item) => {
        const active = item.href === "/" ? pathname === "/" : pathname.startsWith(item.href);
        return (
          <NavItem
            key={item.href}
            {...item}
            layout={row ? "row" : "rail"}
            active={active}
            count={item.href === "/history" ? history.length : undefined}
          />
        );
      })}
    </nav>
  );
}
