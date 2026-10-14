namespace ProjectMD.Api.Auth;

// The three roles, from least to most privileged: viewer < member < admin. The role comes from
// the Firebase custom claim `role` on the ID token (set server-side only, never by the client).
// This mirrors frontend/app/functions/rbac/roles.ts; the backend is the real enforcement.
public static class Roles
{
    // Can read and export scans, nothing else.
    public const string Viewer = "viewer";

    // A normal account: viewer plus creating scans and clearing history.
    public const string Member = "member";

    // Member plus access to the admin area.
    public const string Admin = "admin";

    // Every valid role name.
    public static readonly IReadOnlyList<string> All = [Viewer, Member, Admin];

    // True only for an exact, case-sensitive match of a known role name (no trimming).
    public static bool IsRole(string? value) =>
        value is Viewer or Member or Admin;

    // Turns the raw `role` claim into a role and fails closed. A missing claim (null) means a
    // normal account and becomes "member". A valid role name is used as is. Anything else (wrong
    // case, unknown name, empty text) becomes "viewer", the least privileged role.
    public static string ResolveRole(string? claim)
    {
        if (claim is null)
        {
            return Member;
        }

        return IsRole(claim) ? claim : Viewer;
    }
}
