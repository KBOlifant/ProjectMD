import type { Role } from "./roles";

// The signed-in user as the rest of the app sees them: Firebase identity plus the resolved role.
// Built by the auth provider from the ID token; never persisted by RBAC code.
export interface CurrentUser {
  uid: string;
  email: string | null;
  emailVerified: boolean;
  role: Role;
}
