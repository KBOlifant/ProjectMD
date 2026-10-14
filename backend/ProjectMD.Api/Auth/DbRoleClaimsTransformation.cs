using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ProjectMD.Api.Auth;

// Runs after the Firebase token is validated and replaces whatever `role` claim the token carried
// with the role stored in the database. A token can therefore never raise its own role. The role is
// cached per uid for 30 seconds (evicted when an admin changes it). The same user is also created
// here on first use and promoted when their VERIFIED email is on the Auth:AdminEmails list.
public sealed class DbRoleClaimsTransformation : IClaimsTransformation
{
    // How long a looked-up role is reused.
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly IUserSync _userSync;
    private readonly Data.AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly AuthOptions _options;
    private readonly ILogger<DbRoleClaimsTransformation> _logger;

    // Receives the user upsert helper, the database, the cache, the auth settings and a logger.
    public DbRoleClaimsTransformation(
        IUserSync userSync,
        Data.AppDbContext db,
        IMemoryCache cache,
        IOptions<AuthOptions> options,
        ILogger<DbRoleClaimsTransformation> logger)
    {
        _userSync = userSync;
        _db = db;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    // The cache key for a Firebase uid. Used here and by the admin controller to evict.
    public static string CacheKey(string uid) => "role:" + uid;

    // Returns a copy of the principal whose only `role` claim is the database role. Safe to run more
    // than once per request. A database failure throws RoleLookupUnavailableException (503).
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return principal;
        }

        var uid = Value(principal, FirebaseClaims.Subject) ?? Value(principal, FirebaseClaims.UserId);
        if (uid is null)
        {
            return principal;
        }

        var key = CacheKey(uid);
        if (!_cache.TryGetValue(key, out string? role) || role is null)
        {
            var email = Value(principal, FirebaseClaims.Email);
            var verified = bool.TryParse(Value(principal, FirebaseClaims.EmailVerified), out var v) && v;
            try
            {
                role = await LookUpAsync(uid, email, verified);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not read the role of a signed-in user from the database.");
                throw new RoleLookupUnavailableException(ex);
            }

            _cache.Set(key, role, CacheDuration);
        }

        return WithRole(principal, role);
    }

    // Finds or creates the user row, applies the admin bootstrap, and returns the resolved role.
    private async Task<string> LookUpAsync(string uid, string? email, bool emailVerified)
    {
        var row = await _userSync.GetOrCreateAsync(new CurrentUser(uid, email, emailVerified, Roles.Viewer));

        if (emailVerified && _options.IsBootstrapAdmin(email) && row.Role != Roles.Admin)
        {
            var from = row.Role;
            row.Role = Roles.Admin;
            await _db.SaveChangesAsync();
            _logger.LogInformation(
                "Bootstrap admin promotion: user {UserId} {From} -> {To}", row.Id, from, Roles.Admin);
        }

        return Roles.FromStored(row.Role);
    }

    // Copies the principal with every existing `role` claim removed and the database role added.
    private static ClaimsPrincipal WithRole(ClaimsPrincipal principal, string role)
    {
        var copy = new ClaimsPrincipal();
        var first = true;
        foreach (var identity in principal.Identities)
        {
            var clone = identity.Clone();
            foreach (var claim in clone.FindAll(FirebaseClaims.Role).ToList())
            {
                clone.RemoveClaim(claim);
            }

            if (first)
            {
                clone.AddClaim(new Claim(FirebaseClaims.Role, role));
                first = false;
            }

            copy.AddIdentity(clone);
        }

        return copy;
    }

    // The first non-empty value of a claim, or null.
    private static string? Value(ClaimsPrincipal principal, string type)
    {
        var value = principal.FindFirst(type)?.Value;
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
