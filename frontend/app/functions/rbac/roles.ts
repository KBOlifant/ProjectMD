// Leaf module: imports nothing from the app, so it can run in a plain script as well as in React.

// Roles from least to most privileged. In server mode the role comes from the Project MD
// database via GET /api/me (the backend decides it; a token claim is ignored). In static mode
// (no backend) it falls back to the token claim `role` or the dev override. Never set by the client.
export const ROLES = ["viewer", "member", "admin"] as const;
export type Role = (typeof ROLES)[number];

// True only when the value is exactly one of the known role names (case-sensitive, no trimming).
// Stores nothing.
export function isRole(value: unknown): value is Role {
  return typeof value === "string" && (ROLES as readonly string[]).includes(value);
}

// Dev-only role override read from NEXT_PUBLIC_DEV_ROLE. Returns null in production builds
// (NODE_ENV is inlined at build time, so this branch is dropped there) or when the variable
// is missing or not a valid role. Stores nothing.
function devRoleOverride(): Role | null {
  if (process.env.NODE_ENV !== "production") {
    const raw = process.env.NEXT_PUBLIC_DEV_ROLE;
    if (isRole(raw)) return raw;
  }
  return null;
}

// Turns the raw `role` claim into a Role, failing closed. A dev override (non-production only)
// wins first. A missing claim (undefined/null) means a normal account and becomes "member".
// A valid role string is used as is. Anything else (wrong case, unknown name, non-string)
// becomes "viewer", the least privileged role. Stores nothing.
export function resolveRole(claim: unknown): Role {
  const override = devRoleOverride();
  if (override) return override;
  if (claim === undefined || claim === null) return "member";
  return isRole(claim) ? claim : "viewer";
}
