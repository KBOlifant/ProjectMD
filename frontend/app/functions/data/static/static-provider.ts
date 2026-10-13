import type { ScanProvider } from "../scan-provider";
import { buildScan } from "./mock-scan";

// MOCK provider (the default): wraps the deterministic buildScan(url) in a resolved promise so
// the rest of the app can treat it exactly like the future API provider. Makes no network
// request, ignores the auth context, and stores nothing.
export const staticScanProvider: ScanProvider = {
  generate(url) {
    return Promise.resolve(buildScan(url));
  },
};
