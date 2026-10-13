import type { ScanProvider } from "./scan-provider";

// STUB: the future API provider. When built, it will call the Project MD backend with
// `Authorization: Bearer <Firebase ID token>` (from ctx.getToken()). The backend runs the Groq
// LLM generation and MUST re-verify the ID token, the role and `email_verified` itself; client
// checks are only a convenience. API keys live server-side only and never in this frontend.
// Nothing here makes a request or touches a key yet.
export const apiScanProvider: ScanProvider = {
  // Not built yet: always rejects so a misconfigured NEXT_PUBLIC_SCAN_PROVIDER fails loudly.
  async generate() {
    throw new Error("API provider not implemented");
  },
};
