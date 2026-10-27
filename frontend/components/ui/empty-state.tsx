import type { ReactNode } from "react";

// Dashed placeholder box with a message and optional action button, shown when there is no data. Stores nothing.
export function EmptyState({ children, action }: { children: ReactNode; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-4 rounded-lg border border-dashed border-border-strong px-6 py-10 text-center inset-shadow-zinc-950 inset-shadow-sm">
      <p className="ts-body max-w-md text-ink-muted">{children}</p>
      {action}
    </div>
  );
}
