using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Tests.Extraction;

// Tests for path templates and the first-party / third-party host rules. Pure, no network.
public class ApiPathAndHostTests
{
    // Numbers, UUIDs, hex and token-looking segments become {id}; readable words stay.
    [Theory]
    [InlineData("/api/users/42", "/api/users/{id}")]
    [InlineData("/api/users/42/orders/7", "/api/users/{id}/orders/{id}")]
    [InlineData("/v1/search", "/v1/search")]
    [InlineData("/api/items/3f2504e0-4f89-11d3-9a0c-0305e82c3301", "/api/items/{id}")]
    [InlineData("/api/items/3f2504e04f8911d39a0c0305e82c3301", "/api/items/{id}")]
    [InlineData("/files/9f1c2b7a4d3e5f60", "/files/{id}")]
    [InlineData("/api/orders/a1b2c3d4", "/api/orders/{id}")]
    [InlineData("/api/session/AbC123xyZ9", "/api/session/{id}")]
    [InlineData("/blog/2024-05-17", "/blog/{id}")]
    [InlineData("/users/jane%40example.com", "/users/{id}")]
    [InlineData("/api/top-10-products", "/api/top-10-products")]
    [InlineData("/api/oauth2/token", "/api/oauth2/token")]
    [InlineData("/assets/app.js", "/assets/app.js")]
    [InlineData("/api/1.0/users", "/api/1.0/users")]
    [InlineData("/", "/")]
    [InlineData("", "/")]
    public void Paths_are_turned_into_templates(string path, string expected)
    {
        Assert.Equal(expected, ApiPathNormalizer.Template(path));
    }

    // A long random token never survives in the template.
    [Fact]
    public void Token_like_segments_are_redacted()
    {
        var token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9SECRETSEGMENT";

        var template = ApiPathNormalizer.Template("/api/reset/" + token);

        Assert.Equal("/api/reset/{id}", template);
        Assert.DoesNotContain("SECRETSEGMENT", template);
    }

    // Very deep paths are cut.
    [Fact]
    public void Deep_paths_are_cut()
    {
        var template = ApiPathNormalizer.Template("/a/b/c/d/e/f/g/h/i/j/k/l/m/n");

        Assert.EndsWith("/...", template);
        Assert.True(template.Length < 60);
    }

    // The registrable domain uses the last two labels, or three for known two-part suffixes.
    [Theory]
    [InlineData("www.example.com", "example.com")]
    [InlineData("api.shop.example.com", "example.com")]
    [InlineData("example.com", "example.com")]
    [InlineData("shop.example.co.uk", "example.co.uk")]
    [InlineData("a.b.example.com.au", "example.com.au")]
    [InlineData("api.example.co.za", "example.co.za")]
    [InlineData("localhost", "localhost")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("WWW.Example.COM.", "example.com")]
    public void Registrable_domain_is_found(string host, string expected)
    {
        Assert.Equal(expected, HostClassifier.RegistrableDomain(host));
    }

    // Subdomains of the page's site are first party; other sites (even with a similar name) are not.
    [Theory]
    [InlineData("api.example.com", "www.example.com", true)]
    [InlineData("example.com", "www.example.com", true)]
    [InlineData("cdn.example.co.uk", "www.example.co.uk", true)]
    [InlineData("example.org", "www.example.com", false)]
    [InlineData("evil-example.com", "www.example.com", false)]
    [InlineData("other.co.uk", "example.co.uk", false)]
    [InlineData("127.0.0.1", "127.0.0.1", true)]
    [InlineData("10.0.0.1", "127.0.0.1", false)]
    public void First_party_is_the_same_registrable_domain(string host, string page, bool expected)
    {
        Assert.Equal(expected, HostClassifier.IsFirstParty(host, page));
    }

    // Well-known services get a category, found by host or by any parent domain; unknown hosts get null.
    [Theory]
    [InlineData("www.google-analytics.com", "analytics")]
    [InlineData("fonts.googleapis.com", "fonts")]
    [InlineData("js.stripe.com", "payments")]
    [InlineData("o123.ingest.sentry.io", "monitoring")]
    [InlineData("d111.cloudfront.net", "cdn")]
    [InlineData("widget.intercom.io", "chat")]
    [InlineData("player.vimeo.com", "video")]
    [InlineData("tpc.googlesyndication.com", "ads")]
    [InlineData("tenant.auth0.com", "auth")]
    [InlineData("api.mapbox.com", "maps")]
    [InlineData("some-random-host.example", null)]
    public void Third_party_categories(string host, string? expected)
    {
        Assert.Equal(expected, HostClassifier.Category(host));
    }
}
