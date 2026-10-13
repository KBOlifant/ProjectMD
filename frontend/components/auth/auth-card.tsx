import type { ReactNode } from "react";

interface AuthCardProps {
  title: string;
  subtitle?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
}

// The panel every auth screen sits in: page heading (the one <h1>), optional intro text, the form
// content, and an optional footer line outside the panel for links to the other screens. There is
// only one card per page, so its heading id is fixed. Stores nothing.
export function AuthCard({ title, subtitle, children, footer }: AuthCardProps) {
  return (
    <>
      <section aria-labelledby="auth-card-title" className="rounded-lg border border-border-subtle bg-bg-surface p-6 sm:p-8">
        <h1 id="auth-card-title" className="ts-h2 text-ink">
          {title}
        </h1>
        {subtitle ? <p className="ts-body mt-2 text-ink-muted">{subtitle}</p> : null}
        <div className="mt-6">{children}</div>
      </section>
      {footer ? <p className="ts-body mt-6 text-center text-ink-muted">{footer}</p> : null}
    </>
  );
}
