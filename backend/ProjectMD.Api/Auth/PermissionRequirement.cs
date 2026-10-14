using Microsoft.AspNetCore.Authorization;

namespace ProjectMD.Api.Auth;

// "The caller must hold this permission." One requirement per policy; the single
// PermissionHandler checks them all against the role matrix.
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    // Names the permission, for example "scan:create".
    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }

    // The permission that is required (one of the Permissions constants).
    public string Permission { get; }
}
