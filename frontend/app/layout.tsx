import type { Metadata } from "next";
import { Inter, JetBrains_Mono } from "next/font/google";
import "./globals.css";
import { AuthProvider } from "@/app/functions/auth/AuthProvider";
import { ScanProvider } from "@/components/scan-provider";

const inter = Inter({
  variable: "--font-inter",
  subsets: ["latin"],
});

const jetbrains = JetBrains_Mono({
  variable: "--font-jetbrains",
  subsets: ["latin"],
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
    <html lang="en" className={`${inter.variable} ${jetbrains.variable} h-full antialiased`}>
      <body className="min-h-full">
        <AuthProvider>
          <ScanProvider>{children}</ScanProvider>
        </AuthProvider>
      </body>
    </html>
  );
}
