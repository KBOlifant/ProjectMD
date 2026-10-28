import type { Metadata } from "next";
import { Inter } from "next/font/google";
import localFont from "next/font/local";
import "./globals.css";
import { AuthProvider } from "@/app/functions/auth/AuthProvider";
import { ScanProvider } from "@/components/scan-provider";

const inter = Inter({
  variable: "--font-inter",
  subsets: ["latin"],
});

const neuPrompter = localFont({
  variable: "--font-neu-prompter",
  src: [
    { path: "./assets/neu-prompter/NeuPrompter-Regular.otf", weight: "400" },
    { path: "./assets/neu-prompter/NeuPrompter-Medium.otf", weight: "500" },
    { path: "./assets/neu-prompter/NeuPrompter-SemiBold.otf", weight: "600" },
    { path: "./assets/neu-prompter/NeuPrompter-Bold.otf", weight: "700" },
  ],
});

export const metadata: Metadata = {
  title: "Project MD",
  description: "Turn any public URL into structured Markdown documentation.",
};

// Root layout wrapping every page: loads fonts, then wraps pages in AuthProvider (who is signed
// in) and ScanProvider (shared scan state, which reads the user, so it sits inside AuthProvider).
// The sidebar Shell is NOT here: it lives in app/(workspace)/layout.tsx so login screens have none.
// Stores nothing.
export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className={`${inter.variable} ${neuPrompter.variable} h-full antialiased`}>
      <body className="min-h-full">
        <AuthProvider>
          <ScanProvider>{children}</ScanProvider>
        </AuthProvider>
      </body>
    </html>
  );
}
