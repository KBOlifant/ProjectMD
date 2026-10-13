import type { Metadata } from "next";
import { ResetForm } from "@/components/auth/reset-form";

export const metadata: Metadata = { title: "Reset password · Project MD" };

// The /reset page. Stores nothing.
export default function ResetPage() {
  return <ResetForm />;
}
