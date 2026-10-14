namespace ProjectMD.Api.Auth;

// The machine-readable reasons a permission can be refused. They are sent to the frontend as the
// `reason` field of the 403 ProblemDetails body so it can show the right message.
public static class DenyReasons
{
    // The email address is not verified (only blocks scan:create). Verifying the email fixes it.
    public const string Unverified = "unverified";

    // The role does not include the permission. Verifying the email would not help.
    public const string Role = "role";
}

// The single place that decides whether someone may do something. Pure (no HttpContext, no
// database), so the whole matrix can be unit-tested. Mirrors denyReason() in the frontend.
public static class PermissionRules
{
    // Returns why the permission is refused, or null when it is allowed. Fails closed: an unknown
    // role or permission is refused. The role is checked before the email so a viewer is told
    // "role" (verifying would not help). scan:create also requires a verified email for every role.
    public static string? DenyReason(string role, bool emailVerified, string permission)
    {
        if (!Permissions.RoleHas(role, permission))
        {
            return DenyReasons.Role;
        }

        if (permission == Permissions.ScanCreate && !emailVerified)
        {
            return DenyReasons.Unverified;
        }

        return null;
    }

    // True when the permission is allowed.
    public static bool IsAllowed(string role, bool emailVerified, string permission) =>
        DenyReason(role, emailVerified, permission) is null;
}
