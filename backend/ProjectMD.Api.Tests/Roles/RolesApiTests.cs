using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectMD.Api.Auth;
using ProjectMD.Api.Models;
using ProjectMD.Api.Tests.Auth;

// The namespace is not "Roles" because that would hide the ProjectMD.Api.Auth.Roles class.
namespace ProjectMD.Api.Tests.RoleTests;

// End-to-end tests of database-backed roles, the admin bootstrap list and the admin endpoints, on the
// real API host with in-memory SQLite. Each test uses its own host so the "how many admins are
// there" rules are not disturbed by other tests. No network and no real database.
public class RolesApiTests
{
    // A unique uid.
    private static string NewUid() => "uid-" + Guid.NewGuid().ToString("N");

    // A client that sends a valid test token for the given person (no role claim unless asked).
    private static HttpClient ClientFor(AuthTestFactory factory, string uid, string? email = null, bool verified = true, string? tokenRole = null)
    {
        var client = factory.CreateClient();
        var token = TestTokens.Create(uid: uid, email: email ?? uid + "@example.com", emailVerified: verified, role: tokenRole);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // Reads a response body as JSON.
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    // Sends PUT /api/admin/users/{id}/role.
    private static Task<HttpResponseMessage> PutRoleAsync(HttpClient client, Guid id, string? role) =>
        client.PutAsJsonAsync($"/api/admin/users/{id}/role", new { role });

    // Sends DELETE /api/admin/users/{id}.
    private static Task<HttpResponseMessage> DeleteUserAsync(HttpClient client, Guid id) =>
        client.DeleteAsync($"/api/admin/users/{id}");

    // The role /api/me reports for the client's user.
    private static async Task<string?> MeRoleAsync(HttpClient client) =>
        (await JsonAsync(await client.GetAsync("/api/me"))).GetProperty("role").GetString();

    // ---- Role source ---------------------------------------------------------------------

    [Fact]
    // A token claiming admin changes nothing: the new user is a member and the row says member.
    public async Task Token_claiming_admin_stays_member()
    {
        using var factory = new AuthTestFactory();
        var uid = NewUid();
        using var client = ClientFor(factory, uid, tokenRole: "admin");

        Assert.Equal("member", await MeRoleAsync(client));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal("member", factory.StoredRole(uid));
    }

    [Fact]
    // The stored role wins over a conflicting token claim, in both directions.
    public async Task Database_role_beats_the_token_claim()
    {
        using var factory = new AuthTestFactory();
        var adminUid = NewUid();
        var viewerUid = NewUid();
        factory.SeedUser(adminUid, "admin");
        factory.SeedUser(viewerUid, "viewer");
        using var admin = ClientFor(factory, adminUid, tokenRole: "viewer");
        using var viewer = ClientFor(factory, viewerUid, tokenRole: "admin");

        Assert.Equal("admin", await MeRoleAsync(admin));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal("viewer", await MeRoleAsync(viewer));
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    // A stored role that is not a known name is treated as viewer.
    public async Task Corrupt_stored_role_fails_closed_to_viewer()
    {
        using var factory = new AuthTestFactory();
        var uid = NewUid();
        factory.SeedUser(uid, "root");
        using var client = ClientFor(factory, uid);

        Assert.Equal("viewer", await MeRoleAsync(client));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/_authprobe/scan-create")).StatusCode);
    }

    [Fact]
    // The role is cached for a short time: a direct database edit is not seen on the next request.
    public async Task Role_is_cached_between_requests()
    {
        using var factory = new AuthTestFactory();
        var uid = NewUid();
        factory.SeedUser(uid, "member");
        using var client = ClientFor(factory, uid);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/_authprobe/admin-access")).StatusCode);

        factory.SeedUser(uid, "admin"); // changed behind the cache's back

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/_authprobe/admin-access")).StatusCode);
    }

    [Fact]
    // Auth:DefaultRole decides the role of brand new users.
    public async Task Default_role_applies_to_new_users()
    {
        using var factory = new AuthTestFactory();
        factory.Settings["Auth:DefaultRole"] = "viewer";
        using var client = ClientFor(factory, NewUid());

        Assert.Equal("viewer", await MeRoleAsync(client));
    }

    // ---- Bootstrap admins ----------------------------------------------------------------

    [Fact]
    // A verified email on the list (any case, with spaces in the setting) is promoted to admin in the database.
    public async Task Bootstrap_promotes_a_verified_email_on_the_list()
    {
        using var factory = new AuthTestFactory();
        factory.Settings["Auth:AdminEmails:0"] = "  Boss@Example.COM ";
        var uid = NewUid();
        using var client = ClientFor(factory, uid, email: "boss@example.com");

        Assert.Equal("admin", await MeRoleAsync(client));
        Assert.Equal("admin", factory.StoredRole(uid));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    // An UNVERIFIED email on the list is not promoted, and an email that is not listed is not either.
    public async Task Bootstrap_ignores_unverified_and_unlisted_emails()
    {
        using var factory = new AuthTestFactory();
        factory.Settings["Auth:AdminEmails:0"] = "boss@example.com";
        var unverifiedUid = NewUid();
        var strangerUid = NewUid();
        using var unverified = ClientFor(factory, unverifiedUid, email: "boss@example.com", verified: false);
        using var stranger = ClientFor(factory, strangerUid, email: "stranger@example.com");

        Assert.Equal("member", await MeRoleAsync(unverified));
        Assert.Equal("member", factory.StoredRole(unverifiedUid));
        Assert.Equal("member", await MeRoleAsync(stranger));
        Assert.Equal(HttpStatusCode.Forbidden, (await unverified.GetAsync("/api/admin/users")).StatusCode);
    }

    // ---- Admin endpoint access -----------------------------------------------------------

    [Theory]
    [InlineData("viewer", HttpStatusCode.Forbidden)]
    [InlineData("member", HttpStatusCode.Forbidden)]
    [InlineData("admin", HttpStatusCode.OK)]
    // Only admins can list users; everyone else gets a 403 ProblemDetails with reason "role".
    public async Task List_needs_admin(string role, HttpStatusCode expected)
    {
        using var factory = new AuthTestFactory();
        var uid = NewUid();
        factory.SeedUser(uid, role);
        using var client = ClientFor(factory, uid);

        var response = await client.GetAsync("/api/admin/users");

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Forbidden)
        {
            Assert.Equal("role", (await JsonAsync(response)).GetProperty("reason").GetString());
        }
    }

    [Theory]
    [InlineData("viewer")]
    [InlineData("member")]
    // Non-admins cannot change roles either, and nothing changes.
    public async Task Change_role_needs_admin(string role)
    {
        using var factory = new AuthTestFactory();
        var uid = NewUid();
        factory.SeedUser(uid, role);
        var targetId = factory.SeedUser(NewUid(), "member");
        using var client = ClientFor(factory, uid);

        var response = await PutRoleAsync(client, targetId, "admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Without a token the admin routes are 401.
    public async Task Admin_routes_without_a_token_are_401()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/role", new { role = "admin" })).StatusCode);
    }

    [Fact]
    // The list has the contract shape, newest first, and does not expose the Firebase uid.
    public async Task List_returns_users_newest_first()
    {
        using var factory = new AuthTestFactory();
        var adminUid = NewUid();
        factory.SeedUser(adminUid, "admin");
        await Task.Delay(20);
        var newerUid = NewUid();
        factory.SeedUser(newerUid, "viewer", "newer@example.com");
        using var client = ClientFor(factory, adminUid);

        var response = await client.GetAsync("/api/admin/users");
        var body = await JsonAsync(response);

        Assert.Equal(2, body.GetArrayLength());
        var first = body[0];
        Assert.Equal("newer@example.com", first.GetProperty("email").GetString());
        Assert.Equal("viewer", first.GetProperty("role").GetString());
        Assert.True(Guid.TryParse(first.GetProperty("id").GetString(), out _));
        Assert.True(first.TryGetProperty("createdAt", out _));
        Assert.True(first.TryGetProperty("lastSeenAt", out _));
        Assert.DoesNotContain(newerUid, await response.Content.ReadAsStringAsync());
    }

    // ---- Changing roles ------------------------------------------------------------------

    [Fact]
    // An admin changes another user's role; the updated user comes back and is stored.
    public async Task Admin_can_change_a_role()
    {
        using var factory = new AuthTestFactory();
        var adminUid = NewUid();
        var targetUid = NewUid();
        factory.SeedUser(adminUid, "admin");
        var targetId = factory.SeedUser(targetUid, "member");
        using var client = ClientFor(factory, adminUid);

        var response = await PutRoleAsync(client, targetId, "viewer");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(targetId.ToString(), body.GetProperty("id").GetString());
        Assert.Equal("viewer", body.GetProperty("role").GetString());
        Assert.Equal("viewer", factory.StoredRole(targetUid));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("Admin")]
    [InlineData("")]
    [InlineData(null)]
    // An unknown role is 400 with reason "role" and nothing is changed.
    public async Task Unknown_role_is_400(string? role)
    {
        using var factory = new AuthTestFactory();
        var adminUid = NewUid();
        var targetUid = NewUid();
        factory.SeedUser(adminUid, "admin");
        var targetId = factory.SeedUser(targetUid, "member");
        using var client = ClientFor(factory, adminUid);

        var response = await PutRoleAsync(client, targetId, role);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("role", (await JsonAsync(response)).GetProperty("reason").GetString());
        Assert.Equal("member", factory.StoredRole(targetUid));
    }

    [Fact]
    // An unknown user is 404.
    public async Task Unknown_user_is_404()
    {
        using var factory = new AuthTestFactory();
        var adminUid = NewUid();
        factory.SeedUser(adminUid, "admin");
        using var client = ClientFor(factory, adminUid);

        var response = await PutRoleAsync(client, Guid.NewGuid(), "viewer");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    // Changing your own role is refused, even to the same role.
    public async Task Changing_your_own_role_is_refused()
    {
        using var factory = new AuthTestFactory();
        var adminUid = NewUid();
        var adminId = factory.SeedUser(adminUid, "admin");
        factory.SeedUser(NewUid(), "admin");
        using var client = ClientFor(factory, adminUid);

        foreach (var role in new[] { "member", "admin" })
        {
            var response = await PutRoleAsync(client, adminId, role);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("self", (await JsonAsync(response)).GetProperty("reason").GetString());
        }

        Assert.Equal("admin", factory.StoredRole(adminUid));
    }

    [Fact]
    // The last admin cannot be demoted. B is still cached as admin although the database already
    // demoted B (within the 30 s cache), so B passes authorization but A is the only real admin.
    public async Task Last_admin_cannot_be_demoted()
    {
        using var factory = new AuthTestFactory();
        var aUid = NewUid();
        var bUid = NewUid();
        var aId = factory.SeedUser(aUid, "admin");
        factory.SeedUser(bUid, "admin");
        using var b = ClientFor(factory, bUid);
        Assert.Equal("admin", await MeRoleAsync(b)); // B is now cached as admin
        factory.SeedUser(bUid, "member"); // demoted behind the cache's back

        var response = await PutRoleAsync(b, aId, "member");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("last-admin", (await JsonAsync(response)).GetProperty("reason").GetString());
        Assert.Equal("admin", factory.StoredRole(aUid));
    }

    [Fact]
    // Two admins demote each other at the same moment: exactly one succeeds, the other is a 409
    // last-admin (or a 403 if the first change already took away the second caller's access), and
    // an admin is always left.
    public async Task Two_concurrent_demotions_leave_one_admin()
    {
        using var factory = new AuthTestFactory();
        var aUid = NewUid();
        var bUid = NewUid();
        var aId = factory.SeedUser(aUid, "admin");
        var bId = factory.SeedUser(bUid, "admin");
        using var a = ClientFor(factory, aUid);
        using var b = ClientFor(factory, bUid);
        Assert.Equal("admin", await MeRoleAsync(a)); // both cached as admin
        Assert.Equal("admin", await MeRoleAsync(b));

        var results = await Task.WhenAll(PutRoleAsync(a, bId, "member"), PutRoleAsync(b, aId, "member"));

        var codes = results.Select(r => r.StatusCode).OrderBy(c => (int)c).ToList();
        Assert.Contains(HttpStatusCode.OK, codes);
        Assert.Single(codes, c => c == HttpStatusCode.OK);
        var rejected = results.Single(r => r.StatusCode != HttpStatusCode.OK);
        if (rejected.StatusCode == HttpStatusCode.Conflict)
        {
            Assert.Equal("last-admin", (await JsonAsync(rejected)).GetProperty("reason").GetString());
        }
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        }

        var admins = new[] { factory.StoredRole(aUid), factory.StoredRole(bUid) }.Count(r => r == "admin");
        Assert.Equal(1, admins);
    }

    [Fact]
    // Demoting an admin is fine while another admin remains.
    public async Task Admin_can_be_demoted_when_another_admin_remains()
    {
        using var factory = new AuthTestFactory();
        var aUid = NewUid();
        var bUid = NewUid();
        factory.SeedUser(aUid, "admin");
        var bId = factory.SeedUser(bUid, "admin");
        using var a = ClientFor(factory, aUid);

        var response = await PutRoleAsync(a, bId, "viewer");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("viewer", factory.StoredRole(bUid));
    }

    [Fact]
    // After a change the target's cached role is dropped, so the new role applies immediately
    // (promotion and demotion), not 30 seconds later.
    public async Task Cache_is_evicted_after_a_change()
    {
        using var factory = new AuthTestFactory();
        var adminUid = NewUid();
        var targetUid = NewUid();
        factory.SeedUser(adminUid, "admin");
        var targetId = factory.SeedUser(targetUid, "member");
        using var admin = ClientFor(factory, adminUid);
        using var target = ClientFor(factory, targetUid);
        Assert.Equal(HttpStatusCode.Forbidden, (await target.GetAsync("/api/admin/users")).StatusCode); // cached as member

        await PutRoleAsync(admin, targetId, "admin");
        Assert.Equal(HttpStatusCode.OK, (await target.GetAsync("/api/admin/users")).StatusCode);

        await PutRoleAsync(admin, targetId, "viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await target.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    // A role change is logged with ids and roles, but no email address.
    public async Task Role_change_is_logged_without_emails()
    {
        using var factory = new AuthTestFactory();
        var adminUid = NewUid();
        var targetId = factory.SeedUser(NewUid(), "member", "private.person@example.com");
        factory.SeedUser(adminUid, "admin");
        using var admin = ClientFor(factory, adminUid, email: "admin.person@example.com");

        await PutRoleAsync(admin, targetId, "viewer");

        var lines = factory.Logs.Lines;
        Assert.Contains(lines, l => l.Contains("Role changed") && l.Contains(targetId.ToString()) && l.Contains("member") && l.Contains("viewer"));
        Assert.DoesNotContain(lines, l => l.Contains("private.person@example.com") || l.Contains("admin.person@example.com"));
    }

    // ---- Deleting users ------------------------------------------------------------------

    [Theory]
    [InlineData("viewer")]
    [InlineData("member")]
    // Only admins may delete a user: everyone else gets 403 and the user stays.
    public async Task Delete_user_needs_admin(string role)
    {
        using var factory = new AuthTestFactory();
        var callerUid = NewUid();
        var targetUid = NewUid();
        factory.SeedUser(callerUid, role);
        var targetId = factory.SeedUser(targetUid, "member");
        using var client = ClientFor(factory, callerUid);

        var response = await DeleteUserAsync(client, targetId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("member", factory.StoredRole(targetUid));
    }

    [Fact]
    // An admin deletes another user: 204 and the row is gone.
    public async Task Admin_can_delete_a_user()
    {
        using var factory = new AuthTestFactory();
        var adminUid = NewUid();
        var targetUid = NewUid();
        factory.SeedUser(adminUid, "admin");
        var targetId = factory.SeedUser(targetUid, "member");
        using var client = ClientFor(factory, adminUid);

        var response = await DeleteUserAsync(client, targetId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(factory.StoredRole(targetUid));
    }

    [Fact]
    // An unknown user is 404.
    public async Task Deleting_an_unknown_user_is_404()
    {
        using var factory = new AuthTestFactory();
        var adminUid = NewUid();
        factory.SeedUser(adminUid, "admin");
        using var client = ClientFor(factory, adminUid);

        var response = await DeleteUserAsync(client, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    // Deleting your own account is refused with reason "self".
    public async Task Deleting_yourself_is_refused()
    {
        using var factory = new AuthTestFactory();
        var adminUid = NewUid();
        var adminId = factory.SeedUser(adminUid, "admin");
        factory.SeedUser(NewUid(), "admin");
        using var client = ClientFor(factory, adminUid);

        var response = await DeleteUserAsync(client, adminId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("self", (await JsonAsync(response)).GetProperty("reason").GetString());
        Assert.Equal("admin", factory.StoredRole(adminUid));
    }

    [Fact]
    // The last admin cannot be deleted. B is still cached as admin although the database already
    // demoted B, so B passes authorization but A is the only real admin.
    public async Task Last_admin_cannot_be_deleted()
    {
        using var factory = new AuthTestFactory();
        var aUid = NewUid();
        var bUid = NewUid();
        var aId = factory.SeedUser(aUid, "admin");
        factory.SeedUser(bUid, "admin");
        using var b = ClientFor(factory, bUid);
        Assert.Equal("admin", await MeRoleAsync(b));
        factory.SeedUser(bUid, "member");

        var response = await DeleteUserAsync(b, aId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("last-admin", (await JsonAsync(response)).GetProperty("reason").GetString());
        Assert.Equal("admin", factory.StoredRole(aUid));
    }

    [Fact]
    // An admin can be deleted while another admin remains.
    public async Task Admin_can_be_deleted_when_another_admin_remains()
    {
        using var factory = new AuthTestFactory();
        var adminUid = NewUid();
        var otherUid = NewUid();
        factory.SeedUser(adminUid, "admin");
        var otherId = factory.SeedUser(otherUid, "admin");
        using var client = ClientFor(factory, adminUid);

        var response = await DeleteUserAsync(client, otherId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(factory.StoredRole(otherUid));
    }

    [Fact]
    // A deleted user's cached role is dropped, so their next request is not served as an admin.
    public async Task Deleted_admin_loses_access_immediately()
    {
        using var factory = new AuthTestFactory();
        var adminUid = NewUid();
        var otherUid = NewUid();
        factory.SeedUser(adminUid, "admin");
        var otherId = factory.SeedUser(otherUid, "admin");
        using var admin = ClientFor(factory, adminUid);
        using var other = ClientFor(factory, otherUid);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/admin/users")).StatusCode);

        await DeleteUserAsync(admin, otherId);

        // The row is recreated as a fresh member on the next request, so admin access is gone.
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/admin/users")).StatusCode);
    }

    // ---- Database failure ----------------------------------------------------------------

    [Fact]
    // If the role cannot be read, the request is a 503 ProblemDetails and no access is granted,
    // not even to someone who is an admin in the database.
    public async Task Database_failure_is_503_and_never_grants_access()
    {
        using var factory = new AuthTestFactory();
        var adminUid = NewUid();
        // Must be set before the host starts (SeedUser starts it).
        factory.ConfigureServices = services =>
        {
            services.RemoveAll<IUserSync>();
            services.AddScoped<IUserSync, FailingUserSync>();
        };
        factory.SeedUser(adminUid, "admin");
        using var client = ClientFor(factory, adminUid);

        foreach (var path in new[] { "/api/admin/users", "/api/_authprobe/scan-read", "/api/me" })
        {
            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(503, (await JsonAsync(response)).GetProperty("status").GetInt32());
        }
    }

    // A user sync whose database is down.
    private sealed class FailingUserSync : IUserSync
    {
        public Task<AppUser> GetOrCreateAsync(ICurrentUser user, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The database is down.");
    }
}
