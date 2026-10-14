using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Tests.Extraction;

// End-to-end capture through the real PageRequestProxy against a local test server, with no
// browser: the test plays the part of the page by sending the requests a page would make. "site.test"
// is the scanned site; "other.test" is a different site (third party). No external network.
public class ApiCaptureEndToEndTests
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders = new Dictionary<string, string>();

    // Adds the API routes the page "calls".
    private static void MapApi(WebApplication app)
    {
        app.MapGet("/api/users/{id}", (string id) => Results.Json(new { id = 1, name = "secret-name-" + id, tags = new[] { "a" } }));
        app.MapPost("/graphql", async (HttpContext c) =>
        {
            using var reader = new StreamReader(c.Request.Body);
            await reader.ReadToEndAsync();
            return Results.Json(new { data = new { viewer = new { id = "VIEWERCANARY" } } });
        });
        app.MapGet("/collect", () => Results.Content("ok", "text/plain"));
    }

    // Sends one request through the proxy the way the fetcher does.
    private static Task<ProxyOutcome> SendAsync(PageRequestProxy proxy, string url, string type, string method = "GET", string? body = null, bool main = false) =>
        proxy.HandleAsync(
            new ProxyRequest(method, new Uri(url), NoHeaders, body is null ? null : Encoding.UTF8.GetBytes(body), type, main),
            CancellationToken.None);

    // The page loads, then calls /api/users/42, /api/users/43 and a GraphQL endpoint, and a request
    // goes to another site. The catalog groups, templates and classifies them.
    [Fact]
    public async Task Proxy_records_the_calls_a_page_makes()
    {
        await using var site = await LocalSite.StartAsync(MapApi);
        var dns = LocalSite.Dns();
        var guard = LocalSite.Guard(dns);
        using var client = new GuardedHttpClient(guard);
        var recorder = new ApiRecorder();
        var proxy = new PageRequestProxy(client, guard, new ExtractionOptions { RequestTimeoutSeconds = 30 }, recorder);

        await SendAsync(proxy, site.Url("/"), "document", main: true);
        await SendAsync(proxy, site.Url("/site.css"), "stylesheet");
        await SendAsync(proxy, site.Url("/api/users/42?session=SESSIONCANARY"), "fetch");
        await SendAsync(proxy, site.Url("/api/users/43"), "xhr");
        await SendAsync(proxy, site.Url("/graphql"), "fetch", "POST", "{\"query\":\"query GetViewer { viewer { id } }\",\"variables\":{\"x\":\"VARCANARY\"}}");
        await SendAsync(proxy, site.Url("/collect?uid=UIDCANARY", host: "other.test"), "fetch");

        var catalog = recorder.Build()!;

        var users = catalog.Endpoints.Single(e => e.PathTemplate == "/api/users/{id}");
        Assert.Equal(2, users.Count);
        Assert.Equal("GET", users.Method);
        Assert.Equal("site.test", users.Host);
        Assert.Equal("{id:number,name:string,tags:[string]}", users.ResponseShape);
        Assert.Equal(["session"], users.QueryParams);

        var graphql = catalog.Endpoints.Single(e => e.PathTemplate == "/graphql");
        Assert.Equal("query GetViewer", graphql.GraphQlOperation);
        Assert.Equal("{data:{viewer:{id:string}}}", graphql.ResponseShape);

        Assert.Equal(2, catalog.Endpoints.Count);
        Assert.Equal("other.test", Assert.Single(catalog.ThirdParty).Host);

        var text = System.Text.Json.JsonSerializer.Serialize(catalog);
        foreach (var canary in new[] { "SESSIONCANARY", "VARCANARY", "UIDCANARY", "VIEWERCANARY", "secret-name" })
        {
            Assert.DoesNotContain(canary, text);
        }
    }

    // A proxy with no recorder behaves exactly as before.
    [Fact]
    public async Task Proxy_without_a_recorder_still_works()
    {
        await using var site = await LocalSite.StartAsync(MapApi);
        var guard = LocalSite.Guard(LocalSite.Dns());
        using var client = new GuardedHttpClient(guard);
        var proxy = new PageRequestProxy(client, guard, new ExtractionOptions { RequestTimeoutSeconds = 30 });

        var outcome = await SendAsync(proxy, site.Url("/api/users/1"), "fetch");

        Assert.IsType<ProxyOutcome.Fulfil>(outcome);
    }
}

