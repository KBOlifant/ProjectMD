import type { Metadata } from "next";
import { VerifyEmailPanel } from "@/components/auth/verify-email-panel";

export const metadata: Metadata = { title: "Verify your email · Project MD" };

// The /verify-email page. Stores nothing.
export default function VerifyEmailPage() {
  return <VerifyEmailPanel />;
}
