import { RedirectIfAuthed } from "@/app/functions/auth/guards";

// Layout for the signed-out screens (/login, /signup, /reset, /verify-email): a centred column on the
// dark background with the Project.MD brand on top and no sidebar. RedirectIfAuthed sends signed-in
// users on to the app (signed-in users who still need to verify their email may stay on
// /verify-email). The glow behind the column is decorative. The (auth) folder does not change the
// URLs. Stores nothing.
export default function AuthLayout({ children }: LayoutProps<"/">) {
  return (
    <RedirectIfAuthed>
      <div className="relative flex min-h-screen flex-col items-center justify-center overflow-hidden px-4 py-12">
        <div aria-hidden="true" className="hero-glow pointer-events-none absolute inset-0 opacity-60" />
        <div className="relative w-full max-w-md">
          <header className="mb-8 text-center">
            <span className="ts-h2 text-ink">
              Project<span className="text-violet-400">.MD</span>
            </span>
          </header>
          <main>{children}</main>
        </div>
      </div>
    </RedirectIfAuthed>
  );
}
