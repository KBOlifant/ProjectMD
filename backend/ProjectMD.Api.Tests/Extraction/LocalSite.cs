using System.IO.Compression;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectMD.Api.Services;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Tests.Extraction;

// A tiny web server on this machine (Kestrel on 127.0.0.1, random port) that stands in for a
// website in tests. Tests reach it by a made-up host name ("site.test") that a fake DNS resolver
// maps to 127.0.0.1, with a test-only guard that treats loopback as public. It records every
// request it receives, so a test can prove that something was NOT requested.
internal sealed class LocalSite : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly object _gate = new();
    private readonly List<string> _hits = [];

    private LocalSite(WebApplication app, int port)
    {
        _app = app;
        Port = port;
    }

    // The port the server listens on.
    public int Port { get; }

    // Builds an address on this site for the given host name (any name that maps to loopback).
    public string Url(string path, string host = "site.test") => $"http://{host}:{Port}{path}";

    // The paths requested so far (host header included, as "host path").
    public IReadOnlyList<string> Hits
    {
        get
        {
            lock (_gate)
            {
                return [.. _hits];
            }
        }
    }

    // Just the paths requested so far.
    public IReadOnlyList<string> Paths => Hits.Select(h => h[(h.IndexOf(' ') + 1)..]).ToList();

    // Starts a server with the standard test pages, plus whatever extra mapping the test adds.
    public static async Task<LocalSite> StartAsync(Action<WebApplication>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

        LocalSite? site = null;
        app.Use(async (context, next) =>
        {
            site?.Record(context.Request.Host.Host + " " + context.Request.Path.Value + context.Request.QueryString.Value);
            await next();
        });

        app.MapGet("/", () => Results.Content("<html><head><title>Home</title></head><body><main><h1>Home page</h1><p>" + new string('h', 40) + " plain home page text.</p></main></body></html>", "text/html"));
        app.MapGet("/r1", () => Results.Redirect("/r2"));
        app.MapGet("/r2", () => Results.Redirect("/final"));
        app.MapGet("/final", () => Results.Content(
            "<html><head><title>Final</title><script src=\"/s1.js\" defer></script><link rel=\"stylesheet\" href=\"/site.css\"></head>" +
            "<body><main><h1>Final page</h1><p id=\"slot\">Loading...</p><p>Static paragraph that is long enough to keep around.</p></main></body></html>", "text/html"));
        app.MapGet("/s1.js", () => Results.Redirect("/s2.js"));
        app.MapGet("/s2.js", () => Results.Content("document.getElementById('slot').textContent = 'Rendered by JavaScript after a redirect';", "application/javascript"));
        app.MapGet("/site.css", () => Results.Content("body { color: black; }", "text/css"));
        app.MapGet("/loop", () => Results.Redirect("/loop"));
        app.MapGet("/json", () => Results.Json(new { hello = "world" }));
        app.MapGet("/gzip", (HttpContext c) =>
        {
            using var buffer = new MemoryStream();
            using (var zip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            {
                zip.Write(System.Text.Encoding.UTF8.GetBytes("<html><body><p>Compressed page body text that is long enough to read.</p></body></html>"));
            }

            c.Response.Headers.ContentEncoding = "gzip";
            return Results.Bytes(buffer.ToArray(), "text/html; charset=utf-8");
        });
        app.MapGet("/big", () => Results.Content("<html><body><p>" + new string('b', 60000) + "</p></body></html>", "text/html"));
        app.MapGet("/bigscript.js", () => Results.Content(new string('x', 200000), "application/javascript"));
        app.MapGet("/slow", async (HttpContext c) =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), c.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // The client gave up, which is what the test wants.
            }

            return Results.Content("<html></html>", "text/html");
        });
        app.MapGet("/cookie-set", (HttpContext c) =>
        {
            c.Response.Cookies.Append("probe", "123");
            return Results.Redirect("/cookie-check");
        });
        app.MapGet("/cookie-check", (HttpContext c) =>
            c.Request.Cookies.ContainsKey("probe")
                ? Results.Content("<html><body><p>Cookie arrived with the follow-up request, good.</p></body></html>", "text/html")
                : Results.StatusCode(400));
        app.MapGet("/to-internal", () => Results.Redirect("http://internal.test/secret"));
        app.MapGet("/to-metadata", () => Results.Redirect("http://169.254.169.254/latest/meta-data/"));
        app.MapGet("/to-file", () => Results.Redirect("file:///etc/passwd"));
        app.MapGet("/to-internal.js", () => Results.Redirect("http://169.254.169.254/latest/meta-data/"));
        app.MapGet("/with-internal-script", () => Results.Content(
            "<html><body><main><p id=\"s\">before</p><p>This page includes a script that redirects to a cloud metadata address.</p></main><script src=\"/to-internal.js\"></script></body></html>", "text/html"));
        app.MapPost("/echo", async (HttpContext c) =>
        {
            using var reader = new StreamReader(c.Request.Body);
            return Results.Content(await reader.ReadToEndAsync(), "text/plain");
        });

        configure?.Invoke(app);

        await app.StartAsync();
        var port = new Uri(app.Urls.First()).Port;
        site = new LocalSite(app, port);
        return site;
    }

    // Remembers one request.
    private void Record(string hit)
    {
        lock (_gate)
        {
            _hits.Add(hit);
        }
    }

    // A resolver for the test names: site.test and other.test go to loopback; internal.test goes
    // to a private address; anything else does not resolve.
    public static ExtractionTestSupport.FakeDns Dns() =>
        new ExtractionTestSupport.FakeDns()
            .Add("site.test", "127.0.0.1")
            .Add("other.test", "127.0.0.1")
            .Add("internal.test", "10.20.30.40");

    // A guard for tests: like the real one, but loopback counts as public so the local server can
    // stand in for a website. Everything else (10.x, 169.254.x...) is still refused.
    internal static RequestGuard Guard(IDnsResolver dns, int maxRedirects = 8) =>
        new(dns, maxRedirects, address => IPAddress.IsLoopback(address) || UrlValidator.IsPublicAddress(address));

    // Stops the server.
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
