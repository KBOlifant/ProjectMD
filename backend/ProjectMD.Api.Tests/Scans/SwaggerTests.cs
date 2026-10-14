using System.Net;
using System.Text.Json;

namespace ProjectMD.Api.Tests.Scans;

// Checks the Swagger test page: the document is produced, lists the real endpoints, offers the
// bearer-token "Authorize" box, and the page itself loads without signing in.
public sealed class SwaggerTests : IDisposable
{
    private readonly ScansApiFactory _factory = new(monthlyLimit: 10, permitsPerMinute: 100);
    private readonly HttpClient _client;

    // Starts the real host (in the test environment) once per test.
    public SwaggerTests()
    {
        _client = _factory.CreateClient();
    }

    // Stops the test host.
    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // The OpenAPI document is served without a sign-in, lists the endpoints and declares the token box.
    [Fact]
    public async Task Swagger_document_lists_endpoints_and_the_bearer_scheme()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");
        foreach (var path in new[] { "/api/health", "/api/me", "/api/scans", "/api/scans/{id}", "/api/admin/users", "/api/admin/users/{id}/role" })
        {
            Assert.True(paths.TryGetProperty(path, out _), $"missing {path}");
        }

        var scheme = doc.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
    }

    // The interactive page loads without signing in.
    [Fact]
    public async Task Swagger_page_loads_without_signing_in()
    {
        var response = await _client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Protected endpoints still refuse a request with no token, even with Swagger switched on.
    [Fact]
    public async Task Protected_endpoints_still_need_a_token()
    {
        var response = await _client.GetAsync("/api/scans");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
