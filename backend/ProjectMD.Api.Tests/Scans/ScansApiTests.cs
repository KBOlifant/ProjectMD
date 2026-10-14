using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectMD.Api.Controllers;
using ProjectMD.Api.Dtos;
using ProjectMD.Api.Services;

namespace ProjectMD.Api.Tests.Scans;

// End-to-end tests of the scans API through the real host (authentication, policies, controller,
// SQLite instead of PostgreSQL). Each test uses its own people so tests do not affect each other.
public class ScansApiTests : IClassFixture<DefaultScansApiFactory>
{
    private readonly ScansApiFactory _factory;

    // Receives the shared host for this test class.
    public ScansApiTests(DefaultScansApiFactory factory)
    {
        _factory = factory;
    }

    // A unique person id per call so tests never share data.
    private static string NewUid(string name) => $"{name}-{Guid.NewGuid():N}";

    // Reads a response body as JSON.
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    // Posts { url } to /api/scans.
    private static Task<HttpResponseMessage> PostScanAsync(HttpClient client, string? url) =>
        client.PostAsJsonAsync("/api/scans", new { url });

    // --- Authentication and permissions -----------------------------------------------------------

    // Without a token every scans endpoint answers 401.
    [Fact]
    public async Task Requests_without_a_token_get_401()
    {
        var client = _factory.CreateAnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/scans")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/scans/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostScanAsync(client, "example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync("/api/scans")).StatusCode);
    }

    // A viewer can read but cannot create or clear (the same matrix as the frontend).
    [Fact]
    public async Task Viewer_can_read_but_not_create_or_clear()
    {
        var uid = NewUid("viewer");
        await _factory.SeedScanAsync(uid, DateTime.UtcNow);
        var client = _factory.CreateClientFor(uid, role: "viewer");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/scans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostScanAsync(client, "example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync("/api/scans")).StatusCode);
        Assert.Equal(1, await _factory.CountScansAsync(uid));
    }

    // A member whose email is not verified cannot create, and the 403 says why.
    [Fact]
    public async Task Unverified_member_cannot_create()
    {
        var client = _factory.CreateClientFor(NewUid("unverified"), emailVerified: false);

        var response = await PostScanAsync(client, "example.com");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("unverified", (await JsonAsync(response)).GetProperty("reason").GetString());
    }

    // An unverified member can still read and clear (only creating needs a verified email).
    [Fact]
    public async Task Unverified_member_can_read_and_clear()
    {
        var uid = NewUid("unverified");
        await _factory.SeedScanAsync(uid, DateTime.UtcNow);
        var client = _factory.CreateClientFor(uid, emailVerified: false);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/scans")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/scans")).StatusCode);
    }

    // An unknown role claim fails closed (treated as viewer): it cannot create.
    [Fact]
    public async Task Unknown_role_cannot_create()
    {
        var client = _factory.CreateClientFor(NewUid("odd"), role: "superuser");

        Assert.Equal(HttpStatusCode.Forbidden, (await PostScanAsync(client, "example.com")).StatusCode);
    }

    // A missing role claim means member and an admin has every member permission.
    [Theory]
    [InlineData(null)]
    [InlineData("member")]
    [InlineData("admin")]
    public async Task Member_like_roles_can_create(string? role)
    {
        var client = _factory.CreateClientFor(NewUid("person"), role: role);

        Assert.Equal(HttpStatusCode.Created, (await PostScanAsync(client, "example.com")).StatusCode);
    }

    // --- Listing and opening ----------------------------------------------------------------------

    // The list holds only the caller's scans, newest first, in the contract's JSON shape.
    [Fact]
    public async Task List_returns_only_own_scans_newest_first()
    {
        var alice = NewUid("alice");
        var bob = NewUid("bob");
        var older = await _factory.SeedScanAsync(alice, new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc), "older.example.com");
        var newest = await _factory.SeedScanAsync(alice, new DateTime(2026, 3, 7, 10, 0, 0, DateTimeKind.Utc), "newest.example.com");
        var middle = await _factory.SeedScanAsync(alice, new DateTime(2026, 2, 6, 10, 0, 0, DateTimeKind.Utc), "middle.example.com");
        await _factory.SeedScanAsync(bob, new DateTime(2026, 3, 8, 10, 0, 0, DateTimeKind.Utc), "bobs.example.com");

        var response = await _factory.CreateClientFor(alice).GetAsync("/api/scans");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scans = await response.Content.ReadFromJsonAsync<List<ScanDto>>();
        Assert.NotNull(scans);
        Assert.Equal(new[] { newest, middle, older }.Select(id => id.ToString()), scans.Select(s => s.Id));
        Assert.DoesNotContain(scans, s => s.Host == "bobs.example.com");
    }

    // A person with no scans gets an empty array, not an error.
    [Fact]
    public async Task List_is_empty_for_a_new_person()
    {
        var response = await _factory.CreateClientFor(NewUid("fresh")).GetAsync("/api/scans");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(0, body.GetArrayLength());
    }

    // The JSON uses the camelCase names and ISO-8601 dates the frontend expects.
    [Fact]
    public async Task Scan_json_matches_the_frontend_contract()
    {
        var uid = NewUid("alice");
        var id = await _factory.SeedScanAsync(uid, new DateTime(2026, 3, 7, 10, 15, 30, 123, DateTimeKind.Utc), "example.com");

        var body = await JsonAsync(await _factory.CreateClientFor(uid).GetAsync($"/api/scans/{id}"));

        Assert.Equal(id.ToString(), body.GetProperty("id").GetString());
        Assert.Equal("https://example.com/", body.GetProperty("url").GetString());
        Assert.Equal("example.com", body.GetProperty("host").GetString());
        Assert.Equal("2026-03-07T10:15:30.123Z", body.GetProperty("createdAt").GetString());
        Assert.Equal(3000, body.GetProperty("durationMs").GetInt32());
        Assert.Equal(90, body.GetProperty("confidence").GetInt32());
        Assert.Equal(10, body.GetProperty("pages").GetInt32());
        Assert.Equal("Next.js 15", body.GetProperty("tech")[0].GetProperty("name").GetString());
        Assert.True(body.GetProperty("tech")[0].GetProperty("highlighted").GetBoolean());
        Assert.False(body.GetProperty("tech")[1].GetProperty("highlighted").GetBoolean());
        Assert.StartsWith("# example.com", body.GetProperty("markdown").GetString());
    }

    // Opening your own scan works.
    [Fact]
    public async Task Get_own_scan_returns_it()
    {
        var uid = NewUid("alice");
        var id = await _factory.SeedScanAsync(uid, DateTime.UtcNow, "mine.example.com");

        var response = await _factory.CreateClientFor(uid).GetAsync($"/api/scans/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scan = await response.Content.ReadFromJsonAsync<ScanDto>();
        Assert.Equal(id.ToString(), scan!.Id);
        Assert.Equal("mine.example.com", scan.Host);
    }

    // Someone else's scan and a scan that does not exist look exactly the same: 404, never 403.
    [Fact]
    public async Task Get_someone_elses_scan_returns_404_like_a_missing_one()
    {
        var alice = NewUid("alice");
        var bob = NewUid("bob");
        var alicesScan = await _factory.SeedScanAsync(alice, DateTime.UtcNow);
        var bobClient = _factory.CreateClientFor(bob);

        var theirs = await bobClient.GetAsync($"/api/scans/{alicesScan}");
        var missing = await bobClient.GetAsync($"/api/scans/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, theirs.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // The bodies carry the same title and detail (traceId differs per request, so it is not compared).
        var theirsBody = await JsonAsync(theirs);
        var missingBody = await JsonAsync(missing);
        Assert.Equal(missingBody.GetProperty("title").GetString(), theirsBody.GetProperty("title").GetString());
        Assert.Equal(missingBody.GetProperty("status").GetInt32(), theirsBody.GetProperty("status").GetInt32());
        Assert.False(theirsBody.TryGetProperty("detail", out _) ^ missingBody.TryGetProperty("detail", out _));
    }

    // A malformed id is also just a 404.
    [Fact]
    public async Task Get_with_a_malformed_id_returns_404()
    {
        var client = _factory.CreateClientFor(NewUid("alice"));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/scans/not-a-guid")).StatusCode);
    }

    // --- Creating ---------------------------------------------------------------------------------

    // A valid URL gives 201, a Location header, and a scan the caller can open afterwards.
    [Fact]
    public async Task Create_returns_201_with_location_and_saves_the_scan()
    {
        var uid = NewUid("alice");
        var client = _factory.CreateClientFor(uid);

        var response = await PostScanAsync(client, "www.example.com/docs");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ScanDto>();
        Assert.NotNull(created);
        Assert.Equal("https://www.example.com/docs", created.Url);
        Assert.Equal("example.com", created.Host);
        Assert.InRange(created.Confidence, 88, 99);
        Assert.InRange(created.Pages, 4, 23);
        Assert.NotEmpty(created.Tech);
        Assert.Contains("example.com Documentation", created.Markdown);
        Assert.True(created.CreatedAt > DateTime.UtcNow.AddMinutes(-1));

        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith($"/api/scans/{created.Id}", response.Headers.Location!.ToString());

        var fetched = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(created.Id, (await fetched.Content.ReadFromJsonAsync<ScanDto>())!.Id);
        Assert.Equal(1, await _factory.CountScansAsync(uid));
    }

    // The created scan's createdAt is serialised as an ISO-8601 UTC string.
    [Fact]
    public async Task Create_response_uses_an_iso_8601_utc_timestamp()
    {
        var response = await PostScanAsync(_factory.CreateClientFor(NewUid("alice")), "example.com");

        var createdAt = (await JsonAsync(response)).GetProperty("createdAt").GetString();

        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", createdAt);
    }

    // The same host gives the same mock stack for two different people.
    [Fact]
    public async Task Create_is_deterministic_for_the_same_host()
    {
        var a = await (await PostScanAsync(_factory.CreateClientFor(NewUid("a")), "https://example.com/one")).Content.ReadFromJsonAsync<ScanDto>();
        var b = await (await PostScanAsync(_factory.CreateClientFor(NewUid("b")), "https://www.example.com/two")).Content.ReadFromJsonAsync<ScanDto>();

        Assert.Equal(90, a!.Confidence);
        Assert.Equal(22, a.Pages);
        Assert.Equal(a.Confidence, b!.Confidence);
        Assert.Equal(a.Pages, b.Pages);
        Assert.Equal(a.Tech.Select(t => t.Name), b.Tech.Select(t => t.Name));
    }

    // Bad URLs get a 400 ProblemDetails with the frontend's message, and nothing is saved.
    [Theory]
    [InlineData("", UrlValidator.EmptyMessage)]
    [InlineData("   ", UrlValidator.EmptyMessage)]
    [InlineData(null, UrlValidator.EmptyMessage)]
    [InlineData("not a url", UrlValidator.InvalidMessage)]
    [InlineData("ftp://example.com", UrlValidator.SchemeMessage)]
    [InlineData("http://localhost:3000", UrlValidator.PrivateMessage)]
    [InlineData("http://169.254.169.254/latest/meta-data/", UrlValidator.PrivateMessage)]
    [InlineData("http://[::1]/", UrlValidator.PrivateMessage)]
    [InlineData("https://user:pw@example.com", UrlValidator.CredentialsMessage)]
    public async Task Create_with_a_bad_url_returns_400_problem(string? url, string message)
    {
        var uid = NewUid("alice");
        var client = _factory.CreateClientFor(uid);

        var response = await PostScanAsync(client, url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await JsonAsync(response);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal(message, problem.GetProperty("detail").GetString());
        Assert.Equal("url", problem.GetProperty("reason").GetString());
        Assert.Equal(0, await _factory.CountScansAsync(uid));
    }

    // A body without a url field gets the same friendly message as an empty box.
    [Fact]
    public async Task Create_with_an_empty_object_returns_the_enter_a_url_message()
    {
        var client = _factory.CreateClientFor(NewUid("alice"));

        var response = await client.PostAsJsonAsync("/api/scans", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(UrlValidator.EmptyMessage, (await JsonAsync(response)).GetProperty("detail").GetString());
    }

    // An empty JSON body is a 400, not a server error.
    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{ not json")]
    public async Task Create_with_an_unusable_body_returns_400(string body)
    {
        var client = _factory.CreateClientFor(NewUid("alice"));

        var response = await client.PostAsync("/api/scans", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // The full URL (it may carry a query string with tokens) is never written to the logs.
    [Fact]
    public async Task Create_does_not_log_the_query_string()
    {
        var client = _factory.CreateClientFor(NewUid("alice"));

        var response = await PostScanAsync(client, "https://example.com/page?token=SECRET-QUERY-VALUE-123");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.DoesNotContain(_factory.Logs.Lines, line => line.Contains("SECRET-QUERY-VALUE-123"));
        Assert.Contains(_factory.Logs.Lines, line => line.Contains("example.com"));
    }

    // --- Quota ------------------------------------------------------------------------------------

    // Once the monthly allowance is used the API answers 429 with reason "quota" and saves nothing more.
    [Fact]
    public async Task Create_over_the_monthly_limit_returns_429_quota()
    {
        using var factory = new ScansApiFactory(monthlyLimit: 2, permitsPerMinute: 1000);
        var uid = NewUid("alice");
        var client = factory.CreateClientFor(uid);

        Assert.Equal(HttpStatusCode.Created, (await PostScanAsync(client, "one.example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostScanAsync(client, "two.example.com")).StatusCode);
        var third = await PostScanAsync(client, "three.example.com");

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("application/problem+json", third.Content.Headers.ContentType?.MediaType);
        var problem = await JsonAsync(third);
        Assert.Equal("quota", problem.GetProperty("reason").GetString());
        Assert.Equal(2, problem.GetProperty("used").GetInt32());
        Assert.Equal(2, problem.GetProperty("limit").GetInt32());
        Assert.Matches(@"^\d{4}-\d{2}-01T00:00:00\.000Z$", problem.GetProperty("resetsAt").GetString());
        Assert.Equal(2, await factory.CountScansAsync(uid));
    }

    // Reading and clearing still work after the quota is used up.
    [Fact]
    public async Task Reading_and_clearing_still_work_over_the_limit()
    {
        using var factory = new ScansApiFactory(monthlyLimit: 1, permitsPerMinute: 1000);
        var client = factory.CreateClientFor(NewUid("alice"));
        await PostScanAsync(client, "one.example.com");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostScanAsync(client, "two.example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/scans")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/scans")).StatusCode);
    }

    // Each person has their own allowance.
    [Fact]
    public async Task Quota_is_per_person()
    {
        using var factory = new ScansApiFactory(monthlyLimit: 1, permitsPerMinute: 1000);
        var alice = factory.CreateClientFor(NewUid("alice"));
        var bob = factory.CreateClientFor(NewUid("bob"));

        Assert.Equal(HttpStatusCode.Created, (await PostScanAsync(alice, "one.example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostScanAsync(alice, "two.example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostScanAsync(bob, "one.example.com")).StatusCode);
    }

    // Scans from earlier months do not use up this month's allowance.
    [Fact]
    public async Task Quota_counts_only_the_current_utc_month()
    {
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        using var factory = new ScansApiFactory(monthlyLimit: 2, permitsPerMinute: 1000, clock: new FixedTimeProvider(now));
        var uid = NewUid("alice");
        await factory.SeedScanAsync(uid, new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc));
        await factory.SeedScanAsync(uid, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        await factory.SeedScanAsync(uid, new DateTime(2025, 10, 14, 12, 0, 0, DateTimeKind.Utc));
        var client = factory.CreateClientFor(uid);

        Assert.Equal(HttpStatusCode.Created, (await PostScanAsync(client, "one.example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostScanAsync(client, "two.example.com")).StatusCode);
        var third = await PostScanAsync(client, "three.example.com");

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("2026-11-01T00:00:00.000Z", (await JsonAsync(third)).GetProperty("resetsAt").GetString());
    }

    // A failed (400) request does not use up any allowance.
    [Fact]
    public async Task Invalid_urls_do_not_use_the_allowance()
    {
        using var factory = new ScansApiFactory(monthlyLimit: 1, permitsPerMinute: 1000);
        var client = factory.CreateClientFor(NewUid("alice"));

        Assert.Equal(HttpStatusCode.BadRequest, (await PostScanAsync(client, "localhost")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostScanAsync(client, "example.com")).StatusCode);
    }

    // --- Rate limit -------------------------------------------------------------------------------

    // Too many creates in a minute get 429 with reason "rate" and a Retry-After header; other people are unaffected.
    [Fact]
    public async Task Create_is_rate_limited_per_person()
    {
        using var factory = new ScansApiFactory(monthlyLimit: 1000, permitsPerMinute: 2);
        var alice = factory.CreateClientFor(NewUid("alice"));
        var bob = factory.CreateClientFor(NewUid("bob"));

        Assert.Equal(HttpStatusCode.Created, (await PostScanAsync(alice, "one.example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostScanAsync(alice, "two.example.com")).StatusCode);
        var limited = await PostScanAsync(alice, "three.example.com");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        Assert.Equal("rate", (await JsonAsync(limited)).GetProperty("reason").GetString());
        Assert.True(limited.Headers.RetryAfter is not null);

        Assert.Equal(HttpStatusCode.Created, (await PostScanAsync(bob, "one.example.com")).StatusCode);
    }

    // Reading is not rate limited by the create policy.
    [Fact]
    public async Task Reading_is_not_rate_limited()
    {
        using var factory = new ScansApiFactory(monthlyLimit: 1000, permitsPerMinute: 1);
        var client = factory.CreateClientFor(NewUid("alice"));

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/scans")).StatusCode);
        }
    }

    // --- Clearing ---------------------------------------------------------------------------------

    // Clearing deletes the caller's scans only.
    [Fact]
    public async Task Clear_deletes_only_the_callers_scans()
    {
        var alice = NewUid("alice");
        var bob = NewUid("bob");
        await _factory.SeedScanAsync(alice, DateTime.UtcNow);
        await _factory.SeedScanAsync(alice, DateTime.UtcNow.AddMinutes(-5));
        await _factory.SeedScanAsync(bob, DateTime.UtcNow);

        var response = await _factory.CreateClientFor(alice).DeleteAsync("/api/scans");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await _factory.CountScansAsync(alice));
        Assert.Equal(1, await _factory.CountScansAsync(bob));

        var bobsList = await _factory.CreateClientFor(bob).GetFromJsonAsync<List<ScanDto>>("/api/scans");
        Assert.Single(bobsList!);
    }

    // Clearing with nothing to clear is still 204.
    [Fact]
    public async Task Clear_with_no_scans_returns_204()
    {
        var response = await _factory.CreateClientFor(NewUid("fresh")).DeleteAsync("/api/scans");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // --- Storage limit ----------------------------------------------------------------------------

    // A generator that returns more than 1 MB of Markdown.
    private sealed class OversizedGenerator : IScanGenerator
    {
        // Returns a document one byte over the limit.
        public Task<GeneratedScan> GenerateAsync(Uri url, CancellationToken ct) =>
            Task.FromResult(new GeneratedScan(1, 90, 5, [], new string('x', ScansController.MaxMarkdownBytes + 1)));
    }

    // A generator that returns exactly 1 MB of Markdown.
    private sealed class MaxSizeGenerator : IScanGenerator
    {
        // Returns a document exactly at the limit.
        public Task<GeneratedScan> GenerateAsync(Uri url, CancellationToken ct) =>
            Task.FromResult(new GeneratedScan(1, 90, 5, [], new string('x', ScansController.MaxMarkdownBytes)));
    }

    // Too much Markdown fails with a generic 500 ProblemDetails (no internals) and saves nothing.
    [Fact]
    public async Task Oversized_markdown_returns_a_safe_500_and_is_not_saved()
    {
        using var factory = new ScansApiFactory(1000, 1000, configure: services =>
        {
            services.RemoveAll<IScanGenerator>();
            services.AddSingleton<IScanGenerator, OversizedGenerator>();
        });
        var uid = NewUid("alice");

        var response = await PostScanAsync(factory.CreateClientFor(uid), "example.com");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("xxxx", text);
        Assert.DoesNotContain("Exception", text);
        Assert.Equal(0, await factory.CountScansAsync(uid));
    }

    // Exactly 1 MB of Markdown is still accepted.
    [Fact]
    public async Task Markdown_at_the_size_limit_is_saved()
    {
        using var factory = new ScansApiFactory(1000, 1000, configure: services =>
        {
            services.RemoveAll<IScanGenerator>();
            services.AddSingleton<IScanGenerator, MaxSizeGenerator>();
        });

        var response = await PostScanAsync(factory.CreateClientFor(NewUid("alice")), "example.com");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // The wiring uses the MOCK generator by default.
    [Fact]
    public void The_default_generator_is_the_static_mock()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.IsType<StaticScanGenerator>(scope.ServiceProvider.GetRequiredService<IScanGenerator>());
    }
}
