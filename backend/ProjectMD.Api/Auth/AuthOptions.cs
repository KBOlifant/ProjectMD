namespace ProjectMD.Api.Auth;

// Settings from the "Auth" configuration section. Real values live in user-secrets or environment
// variables, never in a tracked file.
public sealed class AuthOptions
{
    // Emails that are promoted to admin the first time they call the API with a VERIFIED email
    // (Auth:AdminEmails, a string array). Promotion only: removing an address does not demote.
    public string[] AdminEmails { get; set; } = [];

    // The role given to new users (Auth:DefaultRole). Only "viewer" or "member" are accepted;
    // anything else falls back to "member" so a typo can never create admins.
    public string DefaultRole { get; set; } = Roles.Member;

    // The default role after validation.
    public string ResolvedDefaultRole =>
        DefaultRole is Roles.Viewer or Roles.Member ? DefaultRole : Roles.Member;

    // True when the email is on the bootstrap list (trimmed, case-insensitive).
    public bool IsBootstrapAdmin(string? email)
    {
        var wanted = email?.Trim();
        if (string.IsNullOrEmpty(wanted))
        {
            return false;
        }

        return AdminEmails.Any(e => !string.IsNullOrWhiteSpace(e)
            && string.Equals(e.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }
}
