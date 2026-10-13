"use client";

// This file is deliberately NOT named Can.tsx: next to can.ts it would collide on Windows and
// macOS, where file names are case-insensitive, and "rbac/Can" would resolve to can.ts.
// Import it as: import { Can } from "@/app/functions/rbac/can-gate";
import type { ReactNode } from "react";
import type { Permission } from "./permissions";
import { useCan } from "./useCan";

// Renders its children only when the current user has `permission`; otherwise renders
// `fallback` (nothing by default). Nothing is shown while auth is loading. Stores nothing.
export function Can({
  permission,
  fallback = null,
  children,
}: {
  permission: Permission;
  fallback?: ReactNode;
  children: ReactNode;
}) {
  const allowed = useCan(permission);
  return <>{allowed ? children : fallback}</>;
}
