import Link from "next/link";
import type { DenyReason } from "@/app/functions/rbac/can";

// One-line explanation shown under the URL box when the user may not start a scan. `reason` comes
// from useDenyReason("scan:create"): "unverified" links to /verify-email, "role" says the account
// is read-only, "signed-out" links to /login. Purely presentational; stores nothing.
export function ScanBlockedNotice({ reason }: { reason: DenyReason }) {
  const link = "text-violet-400 hover:text-ink-hover";

  if (reason === "unverified") {
    return (
      <>
        Scans are off until your email is verified.{" "}
        <Link href="/verify-email" className={link}>
          Verify email →
        </Link>
      </>
    );
  }
  if (reason === "signed-out") {
    return (
      <>
        Sign in to start scans.{" "}
        <Link href="/login" className={link}>
          Sign in →
        </Link>
      </>
    );
  }
  return <>Your account is read-only.</>;
}
