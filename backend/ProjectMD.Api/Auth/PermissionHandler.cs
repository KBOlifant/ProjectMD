using Microsoft.AspNetCore.Authorization;

namespace ProjectMD.Api.Auth;

// Checks every PermissionRequirement against the caller's role and verified-email status. When
// the check fails it records the machine-readable reason ("role" or "unverified") on the failure
// so the 403 response can carry it.
public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    // Allows the request if the role matrix (and the verified-email rule for scan:create) says so,
    // otherwise fails it with a reason. A request with no valid token is left alone: it never
    // reaches a decision here and ends as a 401 challenge instead.
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var user = FirebaseClaims.Read(context.User);
        if (user is null)
        {
            return Task.CompletedTask;
        }

        var reason = PermissionRules.DenyReason(user.Role, user.EmailVerified, requirement.Permission);
        if (reason is null)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail(new AuthorizationFailureReason(this, reason));
        }

        return Task.CompletedTask;
    }
}
