// Runnable check for the RBAC rules (role parsing, permission matrix, verified-email rule).
// Run from frontend/ with: npx tsx app/functions/rbac/can.check.ts
// It touches only the pure modules (no React, no Firebase) and exits non-zero on any failure.
import assert from "node:assert/strict";
import { can, denyReason } from "./can";
import { PermissionError, assertCan } from "./errors";
import { ROLE_PERMISSIONS, type Permission } from "./permissions";
import { ROLES, resolveRole, type Role } from "./roles";
import type { CurrentUser } from "./types";

// The matrix written out independently of permissions.ts: for each permission, the roles that
// have it. Typed as Record<Permission, ...> so adding a permission forces this table to change.
const EXPECTED: Record<Permission, readonly Role[]> = {
  "scan:read": ["viewer", "member", "admin"],
  "scan:export": ["viewer", "member", "admin"],
  "scan:create": ["member", "admin"],
  "history:clear": ["member", "admin"],
  "admin:access": ["admin"],
};
const PERMISSIONS = Object.keys(EXPECTED) as Permission[];

const env = process.env as Record<string, string | undefined>;
let failures = 0;

// Runs one named check and prints PASS or FAIL. A failure is counted, not thrown, so every
// check runs. Stores nothing.
function test(name: string, fn: () => void) {
  try {
    fn();
    console.log(`PASS  ${name}`);
  } catch (error) {
    failures += 1;
    console.log(`FAIL  ${name}`);
    console.log(error instanceof Error ? error.message : error);
  }
}

// Runs `fn` with the given environment variables set (undefined removes one), then restores
// the previous values so checks do not leak into each other.
function withEnv(vars: Record<string, string | undefined>, fn: () => void) {
  const saved: Record<string, string | undefined> = {};
  for (const [key, value] of Object.entries(vars)) {
    saved[key] = env[key];
    if (value === undefined) delete env[key];
    else env[key] = value;
  }
  try {
    fn();
  } finally {
    for (const [key, value] of Object.entries(saved)) {
      if (value === undefined) delete env[key];
      else env[key] = value;
    }
  }
}

// Builds a test user. Verified by default.
function userWith(role: Role, emailVerified = true): CurrentUser {
  return { uid: "u1", email: "a@example.com", emailVerified, role };
}

// Role parsing is checked with the dev override off and a non-production NODE_ENV.
const NO_OVERRIDE = { NEXT_PUBLIC_DEV_ROLE: undefined, NODE_ENV: "test" };

test("resolveRole: missing claim becomes member", () => {
  withEnv(NO_OVERRIDE, () => {
    assert.equal(resolveRole(undefined), "member");
    assert.equal(resolveRole(null), "member");
  });
});

test("resolveRole: valid role strings are kept", () => {
  withEnv(NO_OVERRIDE, () => {
    for (const role of ROLES) assert.equal(resolveRole(role), role);
  });
});

test("resolveRole: anything else fails closed to viewer", () => {
  const junk: unknown[] = [
    "",
    " admin",
    "admin ",
    "Admin",
    "ADMIN",
    "superuser",
    "root",
    "__proto__",
    "constructor",
    "toString",
    0,
    1,
    NaN,
    true,
    false,
    {},
    { role: "admin" },
    [],
    ["admin"],
    () => "admin",
  ];
  withEnv(NO_OVERRIDE, () => {
    for (const claim of junk) assert.equal(resolveRole(claim), "viewer", String(claim));
  });
});

test("resolveRole: dev override applies outside production", () => {
  withEnv({ NEXT_PUBLIC_DEV_ROLE: "admin", NODE_ENV: "development" }, () => {
    assert.equal(resolveRole(undefined), "admin");
    assert.equal(resolveRole("viewer"), "admin");
    assert.equal(resolveRole("garbage"), "admin");
  });
  withEnv({ NEXT_PUBLIC_DEV_ROLE: "viewer", NODE_ENV: "test" }, () => {
    assert.equal(resolveRole("admin"), "viewer");
  });
});

test("resolveRole: invalid dev override is ignored", () => {
  for (const bad of ["", "Admin", "root", "undefined"]) {
    withEnv({ NEXT_PUBLIC_DEV_ROLE: bad, NODE_ENV: "development" }, () => {
      assert.equal(resolveRole(undefined), "member", bad);
      assert.equal(resolveRole("admin"), "admin", bad);
      assert.equal(resolveRole("junk"), "viewer", bad);
    });
  }
});

test("resolveRole: dev override is ignored in production", () => {
  withEnv({ NEXT_PUBLIC_DEV_ROLE: "admin", NODE_ENV: "production" }, () => {
    assert.equal(resolveRole(undefined), "member");
    assert.equal(resolveRole("viewer"), "viewer");
    assert.equal(resolveRole("junk"), "viewer");
  });
});

test("matrix: ROLE_PERMISSIONS matches the expected table exactly", () => {
  for (const role of ROLES) {
    const expected = PERMISSIONS.filter((p) => EXPECTED[p].includes(role)).sort();
    assert.deepEqual([...ROLE_PERMISSIONS[role]].sort(), expected, role);
  }
});

test("matrix: can() for verified users matches the table", () => {
  for (const role of ROLES) {
    for (const permission of PERMISSIONS) {
      const allowed = EXPECTED[permission].includes(role);
      assert.equal(can(userWith(role), permission), allowed, `${role} ${permission}`);
      assert.equal(
        denyReason(userWith(role), permission),
        allowed ? null : "role",
        `${role} ${permission} reason`,
      );
    }
  }
});

test("unverified: only scan:create is blocked, for every role", () => {
  for (const role of ROLES) {
    for (const permission of PERMISSIONS) {
      const inRole = EXPECTED[permission].includes(role);
      const allowed = inRole && permission !== "scan:create";
      assert.equal(can(userWith(role, false), permission), allowed, `${role} ${permission}`);
    }
  }
  assert.equal(denyReason(userWith("member", false), "scan:create"), "unverified");
  assert.equal(denyReason(userWith("admin", false), "scan:create"), "unverified");
  // A viewer lacks the role, so the reason is "role" (verifying the email would not help).
  assert.equal(denyReason(userWith("viewer", false), "scan:create"), "role");
});

test("unverified: emailVerified must be exactly true", () => {
  for (const value of [false, undefined, null, 0, 1, "true", "yes", {}]) {
    const user = { ...userWith("admin"), emailVerified: value as unknown as boolean };
    assert.equal(can(user, "scan:create"), false, String(value));
    assert.equal(denyReason(user, "scan:create"), "unverified", String(value));
  }
});

test("signed out: nothing is allowed", () => {
  for (const permission of PERMISSIONS) {
    assert.equal(can(null, permission), false, permission);
    assert.equal(denyReason(null, permission), "signed-out", permission);
  }
});

test("fail closed: unknown role or permission is refused", () => {
  for (const role of ["root", "Admin", "", "constructor", "__proto__", "toString", undefined]) {
    const user = { ...userWith("viewer"), role: role as unknown as Role };
    for (const permission of PERMISSIONS) {
      assert.equal(can(user, permission), false, `${String(role)} ${permission}`);
      assert.equal(denyReason(user, permission), "role", `${String(role)} ${permission}`);
    }
  }
  const unknown = "scan:delete" as Permission;
  for (const role of ROLES) assert.equal(can(userWith(role), unknown), false, role);
});

test("assertCan: throws PermissionError with permission and reason", () => {
  const cases: [CurrentUser | null, Permission, string][] = [
    [null, "scan:read", "signed-out"],
    [userWith("member", false), "scan:create", "unverified"],
    [userWith("member"), "admin:access", "role"],
  ];
  for (const [user, permission, reason] of cases) {
    assert.throws(
      () => assertCan(user, permission),
      (error: unknown) =>
        error instanceof PermissionError &&
        error instanceof Error &&
        error.permission === permission &&
        error.reason === reason &&
        error.name === "PermissionError" &&
        error.message.length > 0,
      `${permission} ${reason}`,
    );
  }
});

test("assertCan: passes silently when allowed", () => {
  assert.doesNotThrow(() => assertCan(userWith("member"), "scan:create"));
  assert.doesNotThrow(() => assertCan(userWith("viewer", false), "scan:read"));
  assert.doesNotThrow(() => assertCan(userWith("admin"), "admin:access"));
});

if (failures > 0) {
  console.log(`\n${failures} check(s) failed`);
  process.exit(1);
}
console.log("\nAll RBAC checks passed");
