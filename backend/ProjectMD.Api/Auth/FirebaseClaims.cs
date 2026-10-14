using System.Security.Claims;

namespace ProjectMD.Api.Auth;

// Reads the Firebase claims from an authenticated ClaimsPrincipal. Claim names are the raw token
// names because JwtBearer is configured with MapInboundClaims = false. The token itself is never
// available here (only its claims), so nothing in this file can log it.
public static class FirebaseClaims
{
    // The Firebase uid. `user_id` carries the same value and is used as a fallback.
    public const string Subject = "sub";
    public const string UserId = "user_id";

    // The user's email and whether Firebase has verified it.
    public const string Email = "email";
    public const string EmailVerified = "email_verified";

    // The custom claim that holds the role (set server-side only).
    public const string Role = "role";

    // The value type JwtBearer gives to a claim whose JSON value was null.
    private const string JsonNull = "JSON_NULL";

    // Builds the caller from the token claims, or returns null when nobody is signed in or the
    // token has no uid. The role fails closed: no claim = member, a valid name = that role, a
    // wrong name or more than one role value = viewer.
    public static CurrentUser? Read(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var uid = FirstValue(principal, Subject) ?? FirstValue(principal, UserId);
        if (string.IsNullOrWhiteSpace(uid))
        {
            return null;
        }

        var emailVerified = bool.TryParse(FirstValue(principal, EmailVerified), out var verified) && verified;
        return new CurrentUser(uid, FirstValue(principal, Email), emailVerified, ReadRole(principal));
    }

    // Resolves the `role` claim. A JSON array gives several claims with the same name, which is
    // ambiguous, so it is refused (viewer) instead of picking one.
    private static string ReadRole(ClaimsPrincipal principal)
    {
        var roles = principal.FindAll(Role).ToList();
        return roles.Count switch
        {
            0 => Roles.ResolveRole(null),
            1 when roles[0].ValueType == JsonNull => Roles.ResolveRole(null),
            1 => Roles.ResolveRole(roles[0].Value),
            _ => Roles.Viewer,
        };
    }

    // The first non-empty value of a claim, or null when it is missing or empty.
    private static string? FirstValue(ClaimsPrincipal principal, string type)
    {
        var value = principal.FindFirst(type)?.Value;
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
