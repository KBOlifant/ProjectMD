// Turns a hostname into a 2-letter badge label, e.g. "github.com" -> "GI". Falls back to "MD". Stores nothing.
export function initials(host: string): string {
  return host.replace(/[^a-z0-9]/gi, "").slice(0, 2).toUpperCase() || "MD";
}

// Formats milliseconds as seconds with one decimal, e.g. 3000 -> "3.0s". Stores nothing.
export function formatSeconds(ms: number): string {
  return `${(ms / 1000).toFixed(1)}s`;
}

// Formats an ISO date string for display, e.g. "Oct 6, 2026". Stores nothing.
export function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
}
