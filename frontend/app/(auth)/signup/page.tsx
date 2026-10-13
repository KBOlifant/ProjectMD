import type { Metadata } from "next";
import { SignupForm } from "@/components/auth/signup-form";

export const metadata: Metadata = { title: "Create account · Project MD" };

// The /signup page. Stores nothing.
export default function SignupPage() {
  return <SignupForm />;
}
