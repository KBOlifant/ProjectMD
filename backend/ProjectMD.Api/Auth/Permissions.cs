namespace ProjectMD.Api.Auth;

// The permission names. Each one is also the name of an authorization policy, so controllers use
// [Authorize(Policy = Permissions.ScanCreate)]. The role matrix below mirrors
// frontend/app/functions/rbac/permissions.ts; the backend enforces it from the verified ID token.
public static class Permissions
{
    // Create a new scan. Also needs a verified email for every role (see PermissionRules).
    public const string ScanCreate = "scan:create";

    // List and open the caller's own scans.
    public const string ScanRead = "scan:read";

    // Export or download a scan.
    public const string ScanExport = "scan:export";

    // Delete all of the caller's scans.
    public const string HistoryClear = "history:clear";

    // Open the admin area.
    public const string AdminAccess = "admin:access";

    // Every permission, used to register one policy each.
    public static readonly IReadOnlyList<string> All =
        [ScanCreate, ScanRead, ScanExport, HistoryClear, AdminAccess];

    // viewer: read and export. member: viewer plus create and clear. admin: member plus admin.
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ByRole =
        new Dictionary<string, IReadOnlySet<string>>
        {
            [Roles.Viewer] = new HashSet<string> { ScanRead, ScanExport },
            [Roles.Member] = new HashSet<string> { ScanRead, ScanExport, ScanCreate, HistoryClear },
            [Roles.Admin] = new HashSet<string> { ScanRead, ScanExport, ScanCreate, HistoryClear, AdminAccess },
        };

    // True when the role's row in the matrix contains the permission. An unknown role or an
    // unknown permission is refused (fails closed). The verified-email rule is NOT checked here.
    public static bool RoleHas(string role, string permission) =>
        ByRole.TryGetValue(role, out var granted) && granted.Contains(permission);
}
