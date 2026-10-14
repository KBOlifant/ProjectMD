import type { Scan } from "@/lib/types";
import { apiScanProvider } from "./api-provider";
import { isApiMode } from "./mode";
import { staticScanProvider } from "./static/static-provider";

// Naming note: this file is the DATA SOURCE for scans (static mock, or the backend API).
// `components/scan-provider.tsx` is a different thing: the React context that holds the
// in-progress scan (timer, progress, result id) and calls the `scanProvider` exported here.

// Extra info the caller can pass to a provider. `getToken` returns the signed-in user's Firebase
// ID token (or null). Both providers currently ignore it: the API provider gets the token itself
// through app/functions/data/api-client.ts, which also handles the 401 retry.
export interface ScanContext {
  getToken?: () => Promise<string | null>;
}

// A source of finished Scan objects. `generate` resolves with the Scan or rejects with an Error
// whose message is safe to show to the user.
export interface ScanProvider {
  generate(url: string, ctx?: ScanContext): Promise<Scan>;
}

// The provider the app uses, chosen once by NEXT_PUBLIC_SCAN_PROVIDER: "static" (default, mock
// data, no network) or "api" (the backend; needs NEXT_PUBLIC_API_URL). Any other value falls back
// to "static".
export const scanProvider: ScanProvider = isApiMode ? apiScanProvider : staticScanProvider;
