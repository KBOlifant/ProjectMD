import type { Scan } from "@/lib/types";
import { apiErrorMessage, apiFetch } from "./api-client";
import type { ScanProvider } from "./scan-provider";

// API provider (used when NEXT_PUBLIC_SCAN_PROVIDER=api): asks the backend to create the scan. The
// backend validates the Firebase ID token, the role and `email_verified`, enforces the monthly
// quota, generates the docs and saves the scan; this file only sends the URL and returns the Scan
// it gets back (same shape as lib/types.ts). No API keys of any kind live in the frontend.
export const apiScanProvider: ScanProvider = {
  // POST /api/scans { url } -> Scan. On failure it rejects with an Error whose message is ready to
  // show: 403 unverified -> "Verify your email to continue.", 403 role -> "Your account is
  // read-only.", 429 quota -> "You've used this month's scans.", 429 budget -> the daily AI
  // allowance message, other 429 -> "Too many requests. Try again shortly.", 400 and reason "extract"
  // (page blocked, unreachable or an HTTP error) -> the server's own message, unreachable server ->
  // "Can't reach the server".
  async generate(url) {
    try {
      return await apiFetch<Scan>("/api/scans", { method: "POST", body: JSON.stringify({ url }) });
    } catch (err) {
      throw new Error(apiErrorMessage(err, "Could not generate documentation. Try again."));
    }
  },
};
