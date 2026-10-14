import { getIdToken } from "@/app/functions/auth/auth-service";

// Small fetch wrapper for the Project MD backend (api mode only). It adds the signed-in user's
// Firebase ID token as `Authorization: Bearer <token>`, retries once on 401 with a fresh token, and
// turns every failure into an ApiError. Tokens and request/response bodies are never logged or
// stored; the token only ever travels in the Authorization header (never in a URL).

// Backend base URL without a trailing slash. Written out in full so Next inlines it at build time.
const API_URL = (process.env.NEXT_PUBLIC_API_URL || "http://localhost:5080").replace(/\/+$/, "");

const NETWORK_MESSAGE = "Can't reach the server. Check your connection and try again.";
const SESSION_MESSAGE = "Your session has expired. Sign in again.";

// The machine-readable `reason` extension the backend adds to some 403 and 429 responses.
export type ApiErrorReason = "unverified" | "role" | "quota";

// A failed API call. `status` is the HTTP status (0 when the server could not be reached), `title`
// and `detail` come from the ProblemDetails body, and `reason` from its `reason` extension.
// `message` is the server's detail (or title) and is not always safe to show: use apiErrorMessage.
export class ApiError extends Error {
  readonly status: number;
  readonly title: string;
  readonly detail?: string;
  readonly reason?: ApiErrorReason;

  constructor(status: number, title: string, extra: { detail?: string; reason?: ApiErrorReason } = {}) {
    super(extra.detail || title);
    this.name = "ApiError";
    this.status = status;
    this.title = title;
    this.detail = extra.detail;
    this.reason = extra.reason;
  }
}

// Returns the value when it is a non-empty string, otherwise undefined. Stores nothing.
function text(value: unknown): string | undefined {
  return typeof value === "string" && value !== "" ? value : undefined;
}

// Returns the value when it is one of the reasons the backend uses, otherwise undefined.
function toReason(value: unknown): ApiErrorReason | undefined {
  return value === "unverified" || value === "role" || value === "quota" ? value : undefined;
}

// Reads the ProblemDetails fields (title, detail, reason) out of an error response. A body that is
// missing, not JSON or not an object gives an empty result. The body is never logged.
async function readProblem(res: Response): Promise<{ title?: string; detail?: string; reason?: ApiErrorReason }> {
  try {
    const body: unknown = await res.json();
    if (typeof body !== "object" || body === null) return {};
    const fields = body as Record<string, unknown>;
    return { title: text(fields.title), detail: text(fields.detail), reason: toReason(fields.reason) };
  } catch {
    return {};
  }
}

// Gets the Firebase ID token (null when signed out). Pass true to force a fresh one. If Firebase
// cannot produce a token it throws an ApiError instead: "can't reach the server" when it was a
// network problem, otherwise "session expired".
async function readToken(forceRefresh: boolean): Promise<string | null> {
  try {
    return await getIdToken(forceRefresh);
  } catch (err) {
    const code = typeof err === "object" && err !== null ? (err as { code?: unknown }).code : undefined;
    if (code === "auth/network-request-failed") throw new ApiError(0, NETWORK_MESSAGE);
    throw new ApiError(401, SESSION_MESSAGE);
  }
}

// Sends one request with the given token (or none). A network failure (server down, CORS, offline)
// becomes an ApiError with status 0; an aborted request is rethrown unchanged.
async function send(path: string, init: RequestInit, token: string | null): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");
  if (init.body != null && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);
  try {
    return await fetch(API_URL + path, { ...init, headers });
  } catch (err) {
    if (err instanceof DOMException && err.name === "AbortError") throw err;
    throw new ApiError(0, NETWORK_MESSAGE);
  }
}

// Calls the backend (`path` starts with "/api/...") with the user's Firebase ID token and resolves
// with the parsed JSON body (undefined for 204). On 401 it retries once with a force-refreshed
// token. Any other failure rejects with an ApiError carrying status, title, detail and reason.
export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  let token = await readToken(false);
  let res = await send(path, init, token);
  if (res.status === 401 && token !== null) {
    token = await readToken(true);
    res = await send(path, init, token);
  }

  if (!res.ok) {
    const problem = await readProblem(res);
    throw new ApiError(res.status, problem.title ?? (res.statusText || "Request failed"), {
      detail: problem.detail,
      reason: problem.reason,
    });
  }
  if (res.status === 204) return undefined as T;
  try {
    return (await res.json()) as T;
  } catch {
    throw new ApiError(res.status, "The server sent an unexpected response.");
  }
}

// Turns any error from apiFetch into a short message that is safe to show in the UI. Server detail
// text is only used for 400 (the validation messages are written for users); everything else gets
// fixed wording, or `fallback` when nothing more specific applies. Anything that is not an
// ApiError gets `fallback`.
export function apiErrorMessage(err: unknown, fallback: string): string {
  if (!(err instanceof ApiError)) return fallback;
  if (err.status === 0) return NETWORK_MESSAGE;
  if (err.status === 401) return SESSION_MESSAGE;
  if (err.status === 403) {
    if (err.reason === "unverified") return "Verify your email to continue.";
    if (err.reason === "role") return "Your account is read-only.";
    return "You don't have permission to do that.";
  }
  if (err.status === 429) {
    return err.reason === "quota" ? "You've used this month's scans." : "Too many requests. Try again shortly.";
  }
  if (err.status === 400) return err.detail || fallback;
  return fallback;
}
