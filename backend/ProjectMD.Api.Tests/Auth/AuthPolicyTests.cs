using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProjectMD.Api.Auth;

namespace ProjectMD.Api.Tests.Auth;

// Tests the RBAC matrix against the REAL policies registered by AddFirebaseAuth, using claims
// shaped like a Firebase ID token. Mirrors frontend/app/functions/rbac/can.check.ts.
public class AuthPolicyTests : IDisposable
{
    // The matrix written out independently of Permissions.cs: for each permission, the roles that
    // hold it. If a permission is added the tests below stop matching until this table is updated.
    private static readonly Dictionary<string, string[]> Expected = new()
    {
        [Permissions.ScanRead] = ["viewer", "member", "admin"],
        [Permissions.ScanExport] = ["viewer", "member", "admin"],
        [Permissions.ScanCreate] = ["member", "admin"],
        [Permissions.HistoryClear] = ["member", "admin"],
        [Permissions.AdminAccess] = ["admin"],
    };

    private readonly ServiceProvider _provider;
    private readonly IAuthorizationService _authorization;

    // Builds a service provider with only the auth registrations (no web host, no database).
    public AuthPolicyTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddFirebaseAuth();
        _provider = services.BuildServiceProvider();
        _authorization = _provider.GetRequiredService<IAuthorizationService>();
    }

    public void Dispose() => _provider.Dispose();

    // Every (role, permission) pair.
    public static IEnumerable<object[]> RolePermissionPairs() =>
        from role in Roles.All
        from permission in Expected.Keys
        select new object[] { role, permission };

    // Runs one named policy for a principal and returns the reason recorded by our handler
    // ("role" / "unverified"), or null when allowed.
    private async Task<(bool Allowed, string? Reason)> CheckAsync(ClaimsPrincipal user, string permission)
    {
        var result = await _authorization.AuthorizeAsync(user, null, permission);
        var reason = result.Failure?.FailureReasons
            .Where(r => r.Handler is PermissionHandler)
            .Select(r => r.Message)
            .SingleOrDefault();
        return (result.Succeeded, reason);
    }

    [Fact]
    // The policy names are exactly the five permission strings the frontend uses.
    public void Permission_names_are_the_agreed_strings()
    {
        Assert.Equal("scan:create", Permissions.ScanCreate);
        Assert.Equal("scan:read", Permissions.ScanRead);
        Assert.Equal("scan:export", Permissions.ScanExport);
        Assert.Equal("history:clear", Permissions.HistoryClear);
        Assert.Equal("admin:access", Permissions.AdminAccess);
        Assert.Equal(Expected.Keys.OrderBy(k => k), Permissions.All.OrderBy(k => k));
    }

    [Fact]
    // One policy exists per permission and nothing else is invented.
    public async Task One_policy_is_registered_per_permission()
    {
        var provider = _provider.GetRequiredService<IAuthorizationPolicyProvider>();
        foreach (var permission in Permissions.All)
        {
            var policy = await provider.GetPolicyAsync(permission);
            Assert.NotNull(policy);
            Assert.Contains(policy!.Requirements, r => r is PermissionRequirement p && p.Permission == permission);
        }

        Assert.Null(await provider.GetPolicyAsync("scan:delete"));
    }

    [Fact]
    // The fallback policy makes every route without an attribute require a signed-in user.
    public async Task Fallback_policy_requires_an_authenticated_user()
    {
        var provider = _provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var fallback = await provider.GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Contains(fallback!.Requirements, r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }

    [Theory]
    [MemberData(nameof(RolePermissionPairs))]
    // A verified user gets exactly what the matrix says, and a refusal always says "role".
    public async Task Verified_users_get_exactly_the_matrix(string role, string permission)
    {
        var (allowed, reason) = await CheckAsync(TestPrincipal.Create(role, emailVerified: true), permission);

        var expected = Expected[permission].Contains(role);
        Assert.Equal(expected, allowed);
        Assert.Equal(expected ? null : DenyReasons.Role, reason);
    }

    [Theory]
    [MemberData(nameof(RolePermissionPairs))]
    // An unverified email blocks scan:create and nothing else, for every role.
    public async Task Unverified_email_only_blocks_scan_create(string role, string permission)
    {
        var (allowed, reason) = await CheckAsync(TestPrincipal.Create(role, emailVerified: false), permission);

        var holdsPermission = Expected[permission].Contains(role);
        var expected = holdsPermission && permission != Permissions.ScanCreate;
        Assert.Equal(expected, allowed);

        if (!holdsPermission)
        {
            Assert.Equal(DenyReasons.Role, reason);
        }
        else if (permission == Permissions.ScanCreate)
        {
            Assert.Equal(DenyReasons.Unverified, reason);
        }
        else
        {
            Assert.Null(reason);
        }
    }

    [Fact]
    // A viewer cannot create scans, and is told it is a role problem (verifying would not help).
    public async Task Viewer_cannot_create_a_scan()
    {
        var verified = await CheckAsync(TestPrincipal.Create("viewer", emailVerified: true), Permissions.ScanCreate);
        var unverified = await CheckAsync(TestPrincipal.Create("viewer", emailVerified: false), Permissions.ScanCreate);

        Assert.Equal((false, DenyReasons.Role), verified);
        Assert.Equal((false, DenyReasons.Role), unverified);
    }

    [Fact]
    // A member with an unverified email cannot create scans, and the reason says "unverified".
    public async Task Unverified_member_cannot_create_a_scan()
    {
        var result = await CheckAsync(TestPrincipal.Create("member", emailVerified: false), Permissions.ScanCreate);

        Assert.Equal((false, DenyReasons.Unverified), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("")]
    // email_verified must be exactly true: missing or anything else counts as unverified.
    public async Task Email_verified_must_be_exactly_true(string? claimValue)
    {
        var extra = claimValue is null
            ? Array.Empty<Claim>()
            : new[] { new Claim(FirebaseClaims.EmailVerified, claimValue) };
        var user = TestPrincipal.Create("admin", emailVerified: null, extra: extra);

        var result = await CheckAsync(user, Permissions.ScanCreate);

        Assert.Equal((false, DenyReasons.Unverified), result);
    }

    [Fact]
    // An admin holds every permission when verified.
    public async Task Admin_has_every_permission()
    {
        foreach (var permission in Permissions.All)
        {
            var (allowed, reason) = await CheckAsync(TestPrincipal.Create("admin"), permission);
            Assert.True(allowed, permission);
            Assert.Null(reason);
        }
    }

    [Theory]
    [InlineData("superuser")]
    [InlineData("root")]
    [InlineData("Admin")]
    [InlineData("ADMIN")]
    [InlineData("admin ")]
    [InlineData(" member")]
    [InlineData("")]
    [InlineData("__proto__")]
    [InlineData("scan:create")]
    // An unknown role value fails closed to viewer: read and export only, even when verified.
    public async Task Unknown_role_fails_closed_to_viewer(string role)
    {
        var user = TestPrincipal.Create(role, emailVerified: true);

        Assert.Equal(Roles.Viewer, FirebaseClaims.Read(user)!.Role);
        foreach (var permission in Permissions.All)
        {
            var (allowed, _) = await CheckAsync(user, permission);
            var viewerHas = Expected[permission].Contains("viewer");
            Assert.Equal(viewerHas, allowed);
        }
    }

    [Fact]
    // More than one role value (a JSON array in the token) is ambiguous, so it fails closed.
    public async Task Several_role_values_fail_closed_to_viewer()
    {
        var user = TestPrincipal.Create(
            role: null,
            extra: [new Claim("role", "admin"), new Claim("role", "member")]);

        Assert.Equal(Roles.Viewer, FirebaseClaims.Read(user)!.Role);
        Assert.False((await CheckAsync(user, Permissions.ScanCreate)).Allowed);
        Assert.False((await CheckAsync(user, Permissions.AdminAccess)).Allowed);
    }

    [Fact]
    // No role claim means a normal account: member (can create and clear, cannot use admin).
    public async Task Missing_role_claim_is_member()
    {
        var user = TestPrincipal.Create(role: null, emailVerified: true);

        Assert.Equal(Roles.Member, FirebaseClaims.Read(user)!.Role);
        Assert.True((await CheckAsync(user, Permissions.ScanCreate)).Allowed);
        Assert.True((await CheckAsync(user, Permissions.HistoryClear)).Allowed);
        Assert.False((await CheckAsync(user, Permissions.AdminAccess)).Allowed);
    }

    [Fact]
    // A role claim whose JSON value was null is treated like a missing claim (member), like the frontend.
    public void Json_null_role_claim_is_member()
    {
        var user = TestPrincipal.Create(role: null, extra: [new Claim("role", string.Empty, "JSON_NULL")]);

        Assert.Equal(Roles.Member, FirebaseClaims.Read(user)!.Role);
    }

    [Fact]
    // Someone who is not signed in is never allowed and gets no "role"/"unverified" reason
    // (the middleware answers 401 for them).
    public async Task Anonymous_caller_is_refused_everywhere()
    {
        var anonymous = TestPrincipal.Create("admin", authenticated: false);

        foreach (var permission in Permissions.All)
        {
            var (allowed, reason) = await CheckAsync(anonymous, permission);
            Assert.False(allowed, permission);
            Assert.Null(reason);
        }
    }

    [Fact]
    // A principal with no uid is treated as nobody (also covers a hand-made identity).
    public async Task Principal_without_uid_is_refused()
    {
        var noUid = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "admin")], "test"));

        Assert.Null(FirebaseClaims.Read(noUid));
        Assert.False((await CheckAsync(noUid, Permissions.ScanRead)).Allowed);
    }

    [Fact]
    // `user_id` carries the uid when `sub` is absent.
    public void User_id_claim_is_a_fallback_for_sub()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("user_id", "abc")], "test"));

        Assert.Equal("abc", FirebaseClaims.Read(principal)!.Uid);
    }

    [Theory]
    [InlineData(null, "member")]
    [InlineData("viewer", "viewer")]
    [InlineData("member", "member")]
    [InlineData("admin", "admin")]
    [InlineData("", "viewer")]
    [InlineData("Admin", "viewer")]
    [InlineData("owner", "viewer")]
    // ResolveRole: missing -> member, valid -> itself, anything else -> viewer.
    public void ResolveRole_fails_closed(string? claim, string expected)
    {
        Assert.Equal(expected, Roles.ResolveRole(claim));
    }

    [Fact]
    // Permissions.RoleHas fails closed for unknown roles and unknown permissions.
    public void RoleHas_refuses_unknowns()
    {
        Assert.False(Permissions.RoleHas("root", Permissions.ScanRead));
        Assert.False(Permissions.RoleHas(Roles.Admin, "scan:delete"));
        Assert.False(Permissions.RoleHas(string.Empty, Permissions.ScanRead));
    }

    [Fact]
    // The CurrentUser built from claims exposes uid, email, verified flag and the resolved role.
    public void Current_user_is_built_from_claims()
    {
        var user = FirebaseClaims.Read(TestPrincipal.Create("admin", emailVerified: true, uid: "u-9", email: "a@b.co"));

        Assert.NotNull(user);
        Assert.Equal("u-9", user!.Uid);
        Assert.Equal("a@b.co", user.Email);
        Assert.True(user.EmailVerified);
        Assert.Equal("admin", user.Role);
    }

    [Fact]
    // Asking the request-scoped ICurrentUser for a caller that is not signed in is a loud error.
    public void Http_current_user_throws_when_nobody_is_signed_in()
    {
        var accessor = new Microsoft.AspNetCore.Http.HttpContextAccessor
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
        };

        Assert.Throws<InvalidOperationException>(() => new HttpCurrentUser(accessor).Uid);
    }
}
