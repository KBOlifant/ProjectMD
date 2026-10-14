namespace ProjectMD.Api.Auth;

// The signed-in caller, as proven by the verified Firebase ID token. Inject this instead of
// reading HttpContext.User yourself. Only available on requests that passed authentication.
public interface ICurrentUser
{
    // The Firebase uid (the token's `sub`). Never empty.
    string Uid { get; }

    // The email from the token, or null when the sign-in provider gave none.
    string? Email { get; }

    // True only when the token says `email_verified: true`.
    bool EmailVerified { get; }

    // "viewer", "member" or "admin", already resolved (missing claim = member, unknown = viewer).
    string Role { get; }
}

// A plain value implementation of ICurrentUser. FirebaseClaims.Read produces it from a verified
// token, and tests can create one directly.
public sealed record CurrentUser(string Uid, string? Email, bool EmailVerified, string Role) : ICurrentUser;
