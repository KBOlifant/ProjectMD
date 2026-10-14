// True when the app talks to the backend API (NEXT_PUBLIC_SCAN_PROVIDER=api). Any other value, or
// no value at all, means "static": mock scans, history kept in localStorage, no network requests.
// The variable is written out in full so Next can inline it into the browser bundle at build time.
export const isApiMode: boolean = process.env.NEXT_PUBLIC_SCAN_PROVIDER === "api";
