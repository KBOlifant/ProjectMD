"use client";

import { usePathname } from "next/navigation";
import { useHistory } from "@/lib/history-store";
import { BoltIcon, ClockIcon, FileIcon, LoaderIcon } from "@/components/ui/icons";
import { NavItem } from "@/components/ui/nav-item";

const ITEMS = [
  { href: "/", label: "Generate", icon: <BoltIcon /> },
  { href: "/processing", label: "Processing", icon: <LoaderIcon /> },
  { href: "/results", label: "Results", icon: <FileIcon /> },
  { href: "/history", label: "History", icon: <ClockIcon /> },
];

// Main navigation (Generate, Processing, Results, History). Highlights the current route from the
// URL and shows the saved-scan count on History. `row` lays it out horizontally for mobile. Stores nothing.
export function MainNav({ row = false }: { row?: boolean }) {
  const pathname = usePathname();
  const history = useHistory();

  return (
    <nav
      aria-label="Main"
      className={row ? "-mx-2 flex gap-1 overflow-x-auto py-2" : "flex flex-col gap-1"}
    >
      {ITEMS.map((item) => {
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
