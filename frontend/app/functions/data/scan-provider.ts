import type { Scan } from "@/lib/types";
import { apiScanProvider } from "./api-provider";
import { staticScanProvider } from "./static/static-provider";

// Naming note: this file is the DATA SOURCE for scans (static mock now, backend API later).
// `components/scan-provider.tsx` is a different thing: the React context that holds the
// in-progress scan (timer, progress, result id) and calls the `scanProvider` exported here.

// Extra info the caller can pass to a provider. `getToken` returns the signed-in user's Firebase
// ID token (or null) so an API provider can send it as a Bearer token; the static one ignores it.
export interface ScanContext {
  getToken?: () => Promise<string | null>;
}

// A source of finished Scan objects. `generate` resolves with the Scan or rejects with an Error.
export interface ScanProvider {
  generate(url: string, ctx?: ScanContext): Promise<Scan>;
}

// The provider the app uses, chosen once by NEXT_PUBLIC_SCAN_PROVIDER: "static" (default, mock
// data) or "api" (backend, not implemented yet). Any other value falls back to "static".
export const scanProvider: ScanProvider =
  process.env.NEXT_PUBLIC_SCAN_PROVIDER === "api" ? apiScanProvider : staticScanProvider;
