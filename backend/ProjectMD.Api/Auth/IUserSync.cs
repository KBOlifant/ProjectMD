using ProjectMD.Api.Models;

namespace ProjectMD.Api.Auth;

// Keeps the AppUsers table in step with Firebase sign-ins. Controllers that need the caller's
// database id (for example the scans controller) call this once per request.
public interface IUserSync
{
    // Finds the AppUser row for the caller's Firebase uid, creating it on first use, and
    // refreshes Email (when the token has one) and LastSeenAt (UTC, at most once a minute). New
    // rows get the Auth:DefaultRole. Safe to call from two
    // requests at once: if both try to insert, the loser re-reads the winner's row.
    Task<AppUser> GetOrCreateAsync(ICurrentUser user, CancellationToken cancellationToken = default);
}
