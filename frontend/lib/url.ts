export type ParsedUrl = { ok: true; url: string; host: string } | { ok: false; error: string };

const PRIVATE_HOST = /^(localhost|127\.|10\.|192\.168\.|169\.254\.|172\.(1[6-9]|2\d|3[01])\.|0\.)/i;

// Validates and normalises what the user typed in the URL box. Adds https:// if no scheme
// was given, accepts only http(s), and rejects local/private addresses. Returns either
// { ok: true, url, host } or { ok: false, error } with a message to show. Stores nothing.
export function parseUrl(input: string): ParsedUrl {
  const raw = input.trim();
  if (!raw) return { ok: false, error: "Enter a URL to document." };

  const withScheme = /^[a-z][a-z0-9+.-]*:\/\//i.test(raw) ? raw : `https://${raw}`;
  let parsed: URL;
  try {
    parsed = new URL(withScheme);
  } catch {
    return { ok: false, error: "That is not a valid URL. Try https://your-site.com/docs." };
  }

  if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
    return { ok: false, error: "Only http and https URLs are supported." };
  }
  const host = parsed.hostname.replace(/^www\./, "");
  if (!host.includes(".") || PRIVATE_HOST.test(host) || host === "[::1]") {
    return { ok: false, error: "Enter a public URL. Local and private addresses are not supported." };
  }
  return { ok: true, url: parsed.toString(), host };
}
