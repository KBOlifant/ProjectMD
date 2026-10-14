using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ProjectMD.Api.Auth;
using ProjectMD.Api.Data;

namespace ProjectMD.Api.Tests.Auth;

// End-to-end tests of authentication and authorization on the real API host: Firebase-style
// tokens signed with a throwaway test key, the real JWT bearer validation rules, the real
// policies, and the real 401/403 bodies. No network, no real database.
public class AuthEndpointTests : IClassFixture<AuthTestFactory>
{
    private readonly AuthTestFactory _factory;

    // Receives the shared test host.
    public AuthEndpointTests(AuthTestFactory factory)
    {
        _factory = factory;
    }

    // Sends a GET with an optional bearer token.
    private async Task<HttpResponseMessage> GetAsync(string path, string? token = null, string scheme = "Bearer")
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(scheme, token);
        }

        return await client.SendAsync(request);
    }

    // A unique uid so tests sharing the database never touch each other's rows.
    private static string NewUid() => "uid-" + Guid.NewGuid().ToString("N");

    // Reads a response body as JSON.
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>());

    // ---- Anonymous and 401 ----------------------------------------------------------------

    [Fact]
    // The health check stays public.
    public async Task Health_is_anonymous()
    {
        var response = await GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    // No token: 401 with a ProblemDetails body and a Bearer challenge header.
    public async Task Me_without_a_token_is_401_problem_details()
    {
        var response = await GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(401, (await JsonAsync(response)).GetProperty("status").GetInt32());
    }

    [Theory]
    [InlineData("/api/_authprobe/no-attribute")]
    [InlineData("/api/_authprobe/scan-read")]
    [InlineData("/api/_authprobe/scan-export")]
    [InlineData("/api/_authprobe/scan-create")]
    [InlineData("/api/_authprobe/history-clear")]
    [InlineData("/api/_authprobe/admin-access")]
    [InlineData("/api/me")]
    [InlineData("/api/scans")]
    // Without a token every API route except health is 401 (the probe's unannotated route proves
    // the fallback policy covers a forgotten [Authorize]).
    public async Task Protected_routes_without_a_token_are_401(string path)
    {
        var response = await GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    // An explicitly anonymous route still works without a token.
    public async Task Allow_anonymous_route_needs_no_token()
    {
        var response = await GetAsync("/api/_authprobe/anonymous");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    // A route with no attribute is open to any valid signed-in user (any role).
    public async Task Route_without_attribute_allows_a_signed_in_user()
    {
        var response = await GetAsync("/api/_authprobe/no-attribute", TestTokens.Create(role: "viewer"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    // Only the Bearer scheme is understood.
    public async Task Basic_scheme_is_401()
    {
        var response = await GetAsync("/api/me", TestTokens.Create(), scheme: "Basic");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    // A string that is not a JWT at all.
    public async Task Garbage_token_is_401()
    {
        var response = await GetAsync("/api/me", "not-a-jwt");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    // A token that expired more than the 60 s clock skew ago is refused.
    public async Task Expired_token_is_401()
    {
        var now = DateTime.UtcNow;
        var token = TestTokens.Create(notBefore: now.AddHours(-2), expires: now.AddMinutes(-5));

        var response = await GetAsync("/api/me", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    // Slightly expired (inside the 60 s skew) is still accepted.
    public async Task Token_expired_inside_the_clock_skew_is_accepted()
    {
        var now = DateTime.UtcNow;
        var token = TestTokens.Create(uid: NewUid(), notBefore: now.AddHours(-2), expires: now.AddSeconds(-20));

        var response = await GetAsync("/api/me", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    // A token for another Firebase project (wrong audience) is refused.
    public async Task Wrong_audience_is_401()
    {
        var response = await GetAsync("/api/me", TestTokens.Create(audience: "some-other-project"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    // A token from another issuer is refused.
    public async Task Wrong_issuer_is_401()
    {
        var response = await GetAsync("/api/me", TestTokens.Create(issuer: "https://securetoken.google.com/some-other-project"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    // A token signed with a key the server does not trust is refused.
    public async Task Token_signed_with_an_unknown_key_is_401()
    {
        var response = await GetAsync("/api/me", TestTokens.Create(key: TestTokens.UntrustedKey));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    // Algorithm confusion: an HS256 token must not be accepted (only RS256).
    public async Task Hmac_signed_token_is_401()
    {
        var key = new SymmetricSecurityKey(new byte[64]) { KeyId = "test-key" };

        var response = await GetAsync("/api/me", TestTokens.Create(key: key, algorithm: SecurityAlgorithms.HmacSha256));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    // An unsigned token (alg none) is refused.
    public async Task Unsigned_token_is_401()
    {
        var response = await GetAsync("/api/me", TestTokens.Unsigned());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    // A validly signed token with no uid does not identify anyone, so it is refused.
    public async Task Token_without_a_uid_is_401()
    {
        var response = await GetAsync("/api/me", TestTokens.Create(uid: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    // Changing the payload (promoting viewer to admin) breaks the signature.
    public async Task Tampered_payload_is_401()
    {
        var viewer = TestTokens.Create(uid: NewUid(), role: "viewer").Split('.');
        var admin = TestTokens.Create(uid: NewUid(), role: "admin").Split('.');
        var forged = string.Join('.', viewer[0], admin[1], viewer[2]);

        var response = await GetAsync("/api/me", forged);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- GET /api/me ----------------------------------------------------------------------

    [Fact]
    // A valid token returns who the caller is. A missing role claim means member.
    public async Task Me_returns_the_caller_and_usage()
    {
        var uid = NewUid();

        var response = await GetAsync("/api/me", TestTokens.Create(uid: uid, email: "me@example.com", emailVerified: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(uid, body.GetProperty("uid").GetString());
        Assert.Equal("me@example.com", body.GetProperty("email").GetString());
        Assert.True(body.GetProperty("emailVerified").GetBoolean());
        Assert.Equal("member", body.GetProperty("role").GetString());
        Assert.Equal(3, body.GetProperty("usage").GetProperty("used").GetInt32());
        Assert.Equal(10, body.GetProperty("usage").GetProperty("limit").GetInt32());
        Assert.True(body.GetProperty("usage").TryGetProperty("resetsAt", out _));
    }

    [Theory]
    [InlineData("viewer", "viewer")]
    [InlineData("member", "member")]
    [InlineData("admin", "admin")]
    [InlineData("superuser", "viewer")]
    [InlineData("Admin", "viewer")]
    // The role comes from the database and an unknown stored value fails closed to viewer.
    public async Task Me_reports_the_stored_role(string stored, string expected)
    {
        var uid = NewUid();
        _factory.SeedUser(uid, stored);

        var response = await GetAsync("/api/me", TestTokens.Create(uid: uid));

        Assert.Equal(expected, (await JsonAsync(response)).GetProperty("role").GetString());
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("viewer")]
    // The database role beats a conflicting token claim, both for /api/me and for the policies.
    public async Task Database_role_beats_a_conflicting_token_claim(string claim)
    {
        var uid = NewUid();
        var stored = claim == "admin" ? "viewer" : "admin";
        _factory.SeedUser(uid, stored);
        var token = TestTokens.Create(uid: uid, role: claim);

        var me = await GetAsync("/api/me", token);
        var probe = await GetAsync("/api/_authprobe/admin-access", token);

        Assert.Equal(stored, (await JsonAsync(me)).GetProperty("role").GetString());
        Assert.Equal(stored == "admin" ? HttpStatusCode.OK : HttpStatusCode.Forbidden, probe.StatusCode);
    }

    [Fact]
    // A new user whose token claims admin is still a member (a user cannot raise their own role).
    public async Task Token_claiming_admin_stays_member()
    {
        var uid = NewUid();
        var token = TestTokens.Create(uid: uid, role: "admin");

        var me = await GetAsync("/api/me", token);
        var probe = await GetAsync("/api/_authprobe/admin-access", token);

        Assert.Equal("member", (await JsonAsync(me)).GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, probe.StatusCode);
        Assert.Equal("member", _factory.StoredRole(uid));
    }

    [Fact]
    // Role claims of any shape in the token (array, JSON null) are ignored.
    public async Task Odd_role_claims_in_the_token_are_ignored()
    {
        var array = TestTokens.Create(uid: NewUid(), extraClaims: new Dictionary<string, object> { ["role"] = new[] { "admin", "member" } });
        var nulled = TestTokens.Create(uid: NewUid(), extraClaims: new Dictionary<string, object> { ["role"] = null! });

        foreach (var token in new[] { array, nulled })
        {
            var response = await GetAsync("/api/me", token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("member", (await JsonAsync(response)).GetProperty("role").GetString());
        }
    }

    [Fact]
    // An unverified email is reported as such.
    public async Task Me_reports_an_unverified_email()
    {
        var response = await GetAsync("/api/me", TestTokens.Create(uid: NewUid(), emailVerified: false));

        Assert.False((await JsonAsync(response)).GetProperty("emailVerified").GetBoolean());
    }

    [Fact]
    // First call creates the AppUser row, later calls reuse it and move LastSeenAt forward.
    public async Task Me_upserts_the_user_row()
    {
        var uid = NewUid();

        await GetAsync("/api/me", TestTokens.Create(uid: uid, email: "first@example.com"));
        await AgeLastSeenAsync(uid);
        var first = await LoadUserAsync(uid);
        await GetAsync("/api/me", TestTokens.Create(uid: uid, email: "second@example.com"));
        var second = await LoadUserAsync(uid);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal("first@example.com", first.Email);
        Assert.Equal("second@example.com", second.Email);
        Assert.True(second.LastSeenAt > first.LastSeenAt);
        Assert.Equal(first.CreatedAt, second.CreatedAt);
        Assert.Equal(first.Id, AuthFakeUsageService.LastUserId);
    }

    [Fact]
    // A rejected token must not create a user row (authentication stops the request first).
    public async Task Me_does_not_create_a_row_for_a_rejected_token()
    {
        var uid = NewUid();

        await GetAsync("/api/me", TestTokens.Create(uid: uid, key: TestTokens.UntrustedKey));

        Assert.Null(await LoadUserAsync(uid));
    }

    // Pretends the user was last seen five minutes ago (LastSeenAt is only refreshed once a minute).
    private async Task AgeLastSeenAsync(string uid)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.AppUsers.SingleAsync(u => u.FirebaseUid == uid);
        user.LastSeenAt = user.LastSeenAt.AddMinutes(-5);
        await db.SaveChangesAsync();
    }

    // Reads the AppUser row straight from the test database.
    private async Task<ProjectMD.Api.Models.AppUser?> LoadUserAsync(string uid)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(u => u.FirebaseUid == uid);
    }

    // ---- Policies over HTTP ---------------------------------------------------------------

    [Theory]
    // role,   verified, route,           status, reason
    [InlineData("viewer", true, "scan-read", 200, null)]
    [InlineData("viewer", true, "scan-export", 200, null)]
    [InlineData("viewer", true, "scan-create", 403, "role")]
    [InlineData("viewer", false, "scan-create", 403, "role")]
    [InlineData("viewer", true, "history-clear", 403, "role")]
    [InlineData("viewer", true, "admin-access", 403, "role")]
    [InlineData("member", true, "scan-read", 200, null)]
    [InlineData("member", true, "scan-create", 200, null)]
    [InlineData("member", false, "scan-create", 403, "unverified")]
    [InlineData("member", false, "scan-read", 200, null)]
    [InlineData("member", false, "history-clear", 200, null)]
    [InlineData("member", true, "history-clear", 200, null)]
    [InlineData("member", true, "admin-access", 403, "role")]
    [InlineData("admin", true, "scan-create", 200, null)]
    [InlineData("admin", false, "scan-create", 403, "unverified")]
    [InlineData("admin", true, "admin-access", 200, null)]
    [InlineData("admin", true, "history-clear", 200, null)]
    [InlineData("nonsense", true, "scan-create", 403, "role")]
    [InlineData("nonsense", true, "scan-read", 200, null)]
    // The matrix, enforced end to end: status code and the `reason` in the 403 body.
    public async Task Policies_enforce_the_matrix(string role, bool verified, string route, int status, string? reason)
    {
        var uid = NewUid();
        _factory.SeedUser(uid, role);
        var token = TestTokens.Create(uid: uid, emailVerified: verified);

        var response = await GetAsync($"/api/_authprobe/{route}", token);

        Assert.Equal(status, (int)response.StatusCode);
        if (status == 403)
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var body = await JsonAsync(response);
            Assert.Equal(403, body.GetProperty("status").GetInt32());
            Assert.Equal(reason, body.GetProperty("reason").GetString());
        }
    }

    [Fact]
    // A token with no role claim behaves as member over HTTP too.
    public async Task Missing_role_claim_can_create_when_verified()
    {
        var token = TestTokens.Create(uid: NewUid(), role: null, emailVerified: true);

        var response = await GetAsync("/api/_authprobe/scan-create", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    // A token without the email_verified claim counts as unverified.
    public async Task Missing_email_verified_claim_cannot_create()
    {
        var token = TestTokens.Create(uid: NewUid(), emailVerified: null);

        var response = await GetAsync("/api/_authprobe/scan-create", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("unverified", (await JsonAsync(response)).GetProperty("reason").GetString());
    }

    // ---- Logging --------------------------------------------------------------------------

    [Fact]
    // No log line ever contains a token or the Authorization header, for valid and invalid tokens.
    public async Task Tokens_are_never_logged()
    {
        var now = DateTime.UtcNow;
        var tokens = new[]
        {
            TestTokens.Create(uid: NewUid()),
            TestTokens.Create(uid: NewUid(), notBefore: now.AddHours(-2), expires: now.AddMinutes(-5)),
            TestTokens.Create(uid: NewUid(), audience: "other"),
            TestTokens.Create(uid: NewUid(), key: TestTokens.UntrustedKey),
            TestTokens.Unsigned(),
            "garbage.token.value",
        };

        foreach (var token in tokens)
        {
            await GetAsync("/api/me", token);
            await GetAsync("/api/_authprobe/scan-create", token);
        }

        var lines = _factory.Logs.Lines;
        Assert.NotEmpty(lines);
        foreach (var token in tokens)
        {
            Assert.DoesNotContain(lines, line => line.Contains(token, StringComparison.Ordinal));
            // Also no signature or payload segment on its own.
            foreach (var segment in token.Split('.').Where(s => s.Length > 12))
            {
                Assert.DoesNotContain(lines, line => line.Contains(segment, StringComparison.Ordinal));
            }
        }

        Assert.DoesNotContain(lines, line => line.Contains("Bearer ey", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("Authorization", StringComparison.OrdinalIgnoreCase)
            && line.Contains("Bearer", StringComparison.OrdinalIgnoreCase));
    }
}
