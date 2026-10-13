// Guards the `?next=` redirect after login so it can only point inside this app (no open redirect).

// Returns `next` only if it is a same-site path: a single leading "/", not "//", no backslash and
// no scheme. Anything else (missing, empty, "https://evil.com", "//evil.com", "/\evil.com")
// falls back to "/". Stores nothing.
export function safeNext(next: string | null | undefined): string {
  if (typeof next !== "string" || next === "") return "/";
  if (!next.startsWith("/") || next.startsWith("//")) return "/";
  if (next.includes("\\")) return "/";
  // Control characters (tab, newline...) are stripped by browsers and can hide a "//".
  if (/[\u0000-\u001f\u007f]/.test(next)) return "/";
  return next;
}
