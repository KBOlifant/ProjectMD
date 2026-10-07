import type { Metadata } from "next";
import { Inter, JetBrains_Mono } from "next/font/google";
import "./globals.css";
import { ScanProvider } from "@/components/scan-provider";
import { Shell } from "@/components/shell/shell";

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

// Root layout wrapping every page: loads fonts, then wraps pages in ScanProvider (shared scan
// state) and Shell (sidebar + nav). Stores nothing.
export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className={`${inter.variable} ${jetbrains.variable} h-full antialiased`}>
      <body className="min-h-full">
        <ScanProvider>
          <Shell>{children}</Shell>
        </ScanProvider>
      </body>
    </html>
  );
}
