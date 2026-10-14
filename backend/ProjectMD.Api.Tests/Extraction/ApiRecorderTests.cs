using System.Text;
using System.Text.Json;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Tests.Extraction;

// Tests for the API recorder: grouping, first / third party, limits, privacy and the content hash.
// Observations are built by hand; no network and no browser.
public class ApiRecorderTests
{
    private static readonly Uri Page = new("https://www.example.com/");

    // A JSON fetch answered with the given status and body.
    private static ApiObservation Call(string url, string method = "GET", int status = 200, string? json = "{\"ok\":true}", string? requestBody = null, string type = "fetch") =>
        new(method, new Uri(url), type, status, json is null ? "text/plain" : "application/json; charset=utf-8",
            requestBody is null ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(requestBody),
            json is null ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(json));

    // Identical calls that differ only by id are one endpoint with a count and all statuses.
    [Fact]
    public void Calls_are_grouped_by_method_host_and_template()
    {
        var recorder = new ApiRecorder(Page);
        recorder.Record(Call("https://api.example.com/api/users/42", json: "{\"id\":1,\"name\":\"a\"}"));
        recorder.Record(Call("https://api.example.com/api/users/43", status: 404, json: "{\"error\":\"x\"}"));
        recorder.Record(Call("https://api.example.com/api/users/44"));
        recorder.Record(Call("https://api.example.com/api/users", method: "post"));

        var catalog = recorder.Build()!;

        Assert.Equal(2, catalog.Endpoints.Count);
        var get = catalog.Endpoints.Single(e => e.Method == "GET");
        Assert.Equal("/api/users/{id}", get.PathTemplate);
        Assert.Equal("api.example.com", get.Host);
        Assert.Equal(3, get.Count);
        Assert.Equal([200, 404], get.Statuses);
        Assert.Equal("application/json", get.ContentType);
        Assert.Equal("{id:number,name:string}", get.ResponseShape);
        Assert.True(get.FirstParty);
        Assert.Equal("POST", catalog.Endpoints.Single(e => e.Method == "POST").Method);
        Assert.False(catalog.Truncated);
    }

    // Other sites are summarised per host with a category and are never listed as endpoints.
    [Fact]
    public void Third_party_hosts_are_counted_not_listed_as_endpoints()
    {
        var recorder = new ApiRecorder(Page);
        recorder.Record(Call("https://www.google-analytics.com/collect?v=1", json: null));
        recorder.Record(Call("https://www.google-analytics.com/g/collect", json: null));
        recorder.Record(Call("https://unknown-service.example.net/x", json: null));
        recorder.Record(Call("https://js.stripe.com/v3/", json: null, type: "script"));
        recorder.Record(Call("https://api.example.com/api/me"));

        var catalog = recorder.Build()!;

        Assert.Single(catalog.Endpoints);
        Assert.Equal(3, catalog.ThirdParty.Count);
        var analytics = catalog.ThirdParty.Single(t => t.Host == "www.google-analytics.com");
        Assert.Equal(2, analytics.Requests);
        Assert.Equal("analytics", analytics.Category);
        Assert.Equal("payments", catalog.ThirdParty.Single(t => t.Host == "js.stripe.com").Category);
        Assert.Null(catalog.ThirdParty.Single(t => t.Host == "unknown-service.example.net").Category);
    }

    // The first / third party split understands two-part suffixes such as co.uk.
    [Fact]
    public void First_party_split_handles_co_uk()
    {
        var recorder = new ApiRecorder(new Uri("https://www.shop.co.uk/"));
        recorder.Record(Call("https://api.shop.co.uk/v1/items"));
        recorder.Record(Call("https://api.other.co.uk/v1/items"));

        var catalog = recorder.Build()!;

        Assert.Equal("api.shop.co.uk", Assert.Single(catalog.Endpoints).Host);
        Assert.Equal("api.other.co.uk", Assert.Single(catalog.ThirdParty).Host);
    }

    // Scripts, styles and other non-data requests from the same site are not endpoints.
    [Fact]
    public void Only_xhr_and_fetch_calls_become_endpoints()
    {
        var recorder = new ApiRecorder(Page);
        recorder.Record(Call("https://www.example.com/app.js", json: null, type: "script"));
        recorder.Record(Call("https://www.example.com/site.css", json: null, type: "stylesheet"));
        recorder.Record(Call("https://www.example.com/api/x", type: "xhr"));

        var catalog = recorder.Build()!;

        Assert.Equal("/api/x", Assert.Single(catalog.Endpoints).PathTemplate);
    }

    // GraphQL: one endpoint per operation, including inside a batched body.
    [Fact]
    public void Graphql_operations_become_separate_endpoints()
    {
        var recorder = new ApiRecorder(Page);
        recorder.Record(Call("https://www.example.com/graphql", "POST", requestBody: "{\"query\":\"query GetUser { u }\",\"variables\":{\"id\":\"VAR1\"}}"));
        recorder.Record(Call("https://www.example.com/graphql", "POST", requestBody: "{\"query\":\"query GetUser { u }\"}"));
        recorder.Record(Call("https://www.example.com/graphql", "POST", requestBody: "[{\"query\":\"mutation Save { s }\"},{\"query\":\"{ a }\"}]"));

        var catalog = recorder.Build()!;

        var operations = catalog.Endpoints.Select(e => e.GraphQlOperation).OrderBy(o => o, StringComparer.Ordinal).ToList();
        Assert.Equal(["anonymous query", "mutation Save", "query GetUser"], operations);
        Assert.Equal(2, catalog.Endpoints.Single(e => e.GraphQlOperation == "query GetUser").Count);
    }

    // Query-string values never survive; names are kept, sorted, at most ten.
    [Fact]
    public void Query_parameters_keep_names_only_sorted_and_capped()
    {
        var recorder = new ApiRecorder(Page);
        var query = string.Join("&", Enumerable.Range(1, 12).Select(i => $"p{i:00}=v{i}")) + "&token=SECRETVALUE&b=2&a=1";
        recorder.Record(Call("https://www.example.com/api/list?" + query));

        var endpoint = Assert.Single(recorder.Build()!.Endpoints);

        Assert.Equal(10, endpoint.QueryParams.Count);
        Assert.Equal(endpoint.QueryParams.OrderBy(p => p, StringComparer.Ordinal).ToList(), endpoint.QueryParams);
        Assert.DoesNotContain("SECRETVALUE", string.Concat(endpoint.QueryParams));
    }

    // At most 40 first-party endpoints are returned, busiest first, and Truncated is set.
    [Fact]
    public void Endpoints_are_truncated_at_forty()
    {
        var recorder = new ApiRecorder(Page);
        for (var i = 0; i < 50; i++)
        {
            recorder.Record(Call($"https://www.example.com/api/r{i:00}x/list"));
        }

        recorder.Record(Call("https://www.example.com/api/busy"));
        recorder.Record(Call("https://www.example.com/api/busy"));

        var catalog = recorder.Build()!;

        Assert.Equal(40, catalog.Endpoints.Count);
        Assert.True(catalog.Truncated);
        Assert.Equal("/api/busy", catalog.Endpoints[0].PathTemplate);
    }

    // Nothing recorded (or a failed capture) gives null, never an exception.
    [Fact]
    public void Empty_recorder_builds_null()
    {
        Assert.Null(new ApiRecorder(Page).Build());
    }

    // Canary strings placed in every place a value could hide never reach the catalog.
    [Fact]
    public void No_value_from_a_request_or_response_appears_in_the_catalog()
    {
        var recorder = new ApiRecorder(Page);
        var canaries = new[] { "CANARY_QUERY", "CANARY_BODY", "CANARY_VAR", "CANARY_TOKEN", "CANARY_FRAGMENT", "CANARY_USER", "CANARY_FIELDVALUE" };

        recorder.Record(Call(
            "https://CANARY_USER:pw@api.example.com/api/users/9f1c2b7a4d3e5f60/CANARY_TOKEN_aB3dE5gH7jK9mN1pQ3?email=CANARY_QUERY&token=CANARY_QUERY#CANARY_FRAGMENT",
            json: "{\"email\":\"CANARY_FIELDVALUE\",\"password\":\"CANARY_FIELDVALUE\",\"list\":[\"CANARY_FIELDVALUE\"]}"));
        recorder.Record(Call(
            "https://www.example.com/graphql",
            "POST",
            requestBody: "{\"query\":\"query Login { login(p: \\\"CANARY_BODY\\\") }\",\"variables\":{\"password\":\"CANARY_VAR\"}}",
            json: "{\"data\":{\"login\":\"CANARY_FIELDVALUE\"}}"));
        recorder.Record(Call("https://tracker.example.net/c?id=CANARY_QUERY", json: null));

        var serialized = JsonSerializer.Serialize(recorder.Build());

        foreach (var canary in canaries)
        {
            Assert.DoesNotContain(canary, serialized);
        }

        Assert.Contains("password:string", serialized);
    }

    // The same calls with different counts and statuses give the same canonical form (so the content
    // hash is stable), while a new endpoint changes it.
    [Fact]
    public void Content_hash_ignores_counts_and_statuses_but_sees_new_endpoints()
    {
        string HashFor(Action<ApiRecorder> record)
        {
            var recorder = new ApiRecorder(Page);
            record(recorder);
            var raw = ExtractionTestSupport.Raw(ExtractionTestSupport.SamplePage()) with { Api = recorder.Build() };
            return new HtmlReducer().Reduce(raw, new ReduceOptions()).ContentHash;
        }

        var baseline = HashFor(r =>
        {
            r.Record(Call("https://api.example.com/api/users/1"));
            r.Record(Call("https://api.example.com/graphql", "POST", requestBody: "{\"query\":\"query A { a }\"}"));
        });

        var moreCallsOtherStatus = HashFor(r =>
        {
            r.Record(Call("https://api.example.com/graphql", "POST", requestBody: "{\"query\":\"query A { a }\"}"));
            r.Record(Call("https://api.example.com/api/users/2", status: 500));
            r.Record(Call("https://api.example.com/api/users/3"));
            r.Record(Call("https://api.example.com/graphql", "POST", requestBody: "{\"query\":\"query A { a }\"}"));
        });

        var withNewEndpoint = HashFor(r =>
        {
            r.Record(Call("https://api.example.com/api/users/1"));
            r.Record(Call("https://api.example.com/graphql", "POST", requestBody: "{\"query\":\"query A { a }\"}"));
            r.Record(Call("https://api.example.com/api/orders"));
        });

        var withoutApi = new HtmlReducer().Reduce(ExtractionTestSupport.Raw(ExtractionTestSupport.SamplePage()), new ReduceOptions()).ContentHash;

        Assert.Equal(baseline, moreCallsOtherStatus);
        Assert.NotEqual(baseline, withNewEndpoint);
        Assert.NotEqual(baseline, withoutApi);
    }

    // The reducer passes the captured data on to the digest, and pages with no capture keep a null.
    [Fact]
    public void Reducer_copies_api_and_design_into_the_digest()
    {
        var recorder = new ApiRecorder(Page);
        recorder.Record(Call("https://api.example.com/api/me"));
        var design = new DesignSystem([new ColorToken("#ffffff", 3, "background")], [], [], [], [], [], [], null);
        var raw = ExtractionTestSupport.Raw(ExtractionTestSupport.SamplePage()) with { Api = recorder.Build(), Design = design };

        var digest = new HtmlReducer().Reduce(raw, new ReduceOptions());

        Assert.Single(digest.Api!.Endpoints);
        Assert.Same(design, digest.Design);
        Assert.Null(ExtractionTestSupport.Reduce(ExtractionTestSupport.SamplePage()).Api);
    }

    // Design facts are part of the hash too, without usage counts.
    [Fact]
    public void Content_hash_changes_with_the_design_but_not_with_usage_counts()
    {
        string HashFor(DesignSystem? design) =>
            new HtmlReducer().Reduce(ExtractionTestSupport.Raw(ExtractionTestSupport.SamplePage()) with { Design = design }, new ReduceOptions()).ContentHash;

        DesignSystem Design(string hex, int uses) => new([new ColorToken(hex, uses, "text")], [], [], [], [], [], [], null);

        Assert.Equal(HashFor(Design("#111111", 5)), HashFor(Design("#111111", 99)));
        Assert.NotEqual(HashFor(Design("#111111", 5)), HashFor(Design("#222222", 5)));
    }
}
