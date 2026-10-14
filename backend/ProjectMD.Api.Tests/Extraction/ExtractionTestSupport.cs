using System.Net;
using System.Text;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Tests.Extraction;

// Shared helpers for the extraction tests: fixture pages, raw pages and digests built from them,
// and a fake DNS resolver. Nothing here touches the network or a browser.
internal static class ExtractionTestSupport
{
    // The address the fixtures pretend to be served from.
    public static readonly Uri PageUrl = new("https://www.example-site.com/docs/");

    // A raw page around some HTML.
    public static RawPage Raw(
        string html,
        Uri? url = null,
        int status = 200,
        IReadOnlyDictionary<string, string>? headers = null,
        IReadOnlyList<string>? scripts = null,
        IReadOnlyList<string>? styles = null) =>
        new(url ?? PageUrl, status, html, headers ?? new Dictionary<string, string>(), scripts ?? [], styles ?? []);

    // Reduces HTML with the default options (or the given ones).
    public static PageDigest Reduce(string html, ReduceOptions? options = null, Uri? url = null) =>
        new HtmlReducer().Reduce(Raw(html, url), options ?? new ReduceOptions());

    // A hand-made digest for fingerprint tests; only the signals are filled in.
    public static PageDigest Digest(
        IReadOnlyList<string>? scripts = null,
        IReadOnlyList<string>? styles = null,
        IReadOnlyDictionary<string, string>? headers = null,
        IReadOnlyList<string>? markers = null,
        IReadOnlyList<LinkRef>? links = null,
        string text = "") =>
        new(
            PageUrl, 200, "Title", null, "en", [], text, links ?? [],
            scripts ?? [], styles ?? [], headers ?? new Dictionary<string, string>(),
            [], 1000, 100, 25, new string('a', 64), markers);

    // A small but typical page: header, navigation, main content, footer, plus all kinds of noise.
    // The nonce parameter changes only values that must not affect the content hash.
    public static string SamplePage(string nonce = "n-111", string session = "9f2c4b7a1d3e5f60") =>
        $$"""
        <!DOCTYPE html>
        <html lang="en-GB">
        <head>
          <meta charset="utf-8">
          <title>  Acme Docs |  Getting started  </title>
          <meta name="description" content="Everything you need to start building with Acme.">
          <meta property="og:site_name" content="Acme">
          <meta name="csrf-token" content="{{nonce}}-csrf-token-value">
          <link rel="stylesheet" href="/assets/site.css">
          <style>.hero { color: red; } {{new string('x', 500)}}</style>
          <script nonce="{{nonce}}">window.__STATE__ = {"session":"{{session}}"};</script>
          <script type="application/ld+json">
            {"@context":"https://schema.org","@type":"Organization","name":"Acme","url":"https://www.example-site.com",
             "description":"We make anvils.","potentialAction":{"@type":"SearchAction"},"empty":"","nothing":null}
          </script>
          <script type="application/ld+json">{ this is not json </script>
        </head>
        <body class="home">
          <!-- a comment that must disappear -->
          <div id="cookie-banner" class="cookie-consent">We use cookies to improve your experience. <button>Accept all cookies</button></div>
          <header>
            <a href="/">Acme</a>
            <nav>
              <a href="/docs/install">Install</a>
              <a href="/docs/usage">Usage</a>
              <a href="https://github.com/acme/acme">GitHub</a>
            </nav>
          </header>
          <main>
            <h1>Getting started</h1>
            <p>Acme lets you build reliable anvils quickly. This guide walks through installation and first use.</p>
            <h2>Install</h2>
            <p>Run the installer and follow the prompts to set up the command line tool on your machine.</p>
            <ul><li>Works on Windows, macOS and Linux with no extra setup required.</li><li>Ok</li></ul>
            <h3>Requirements</h3>
            <p>You need a recent operating system and about two hundred megabytes of free disk space.</p>
            <h5>Not in the outline</h5>
            <p hidden>Hidden paragraph that nobody sees.</p>
            <p aria-hidden="true">Aria hidden paragraph.</p>
            <p style="display:none">Display none paragraph.</p>
            <svg width="10" height="10"><title>icon title</title><path d="M0 0h10v10z"/></svg>
            <img src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==" alt="pixel">
            <noscript>Please enable JavaScript to see this page.</noscript>
            <iframe src="https://ads.example.net/frame"></iframe>
            <table><tr><th>Plan</th><th>Price</th></tr><tr><td>Basic</td><td>Free forever</td></tr></table>
            <p>Questions? <a href="/docs/usage">Read the usage guide</a>, <a href="mailto:help@example-site.com">email us</a> or <a href="javascript:void(0)">click here</a>.</p>
            <a href="#top">Back to top</a>
            <a href="/docs/usage#section">Usage again</a>
            <a href="/docs/"><img src="/logo.png" alt="Docs home"></a>
          </main>
          <footer>
            <h4>Company</h4>
            <p>Copyright Acme Inc. All rights reserved worldwide.</p>
            <a href="/legal/privacy">Privacy</a>
          </footer>
          <script src="https://cdn.example.net/app.js"></script>
        </body>
        </html>
        """;

    // A page padded with the kind of weight real pages carry: big inline scripts, CSS and SVG.
    public static string HeavyPage()
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><title>Heavy page</title>");
        sb.Append("<meta name=\"description\" content=\"A page with a lot of weight.\">");
        sb.Append("<style>");
        for (var i = 0; i < 400; i++)
        {
            sb.Append($".class-{i} {{ margin: {i}px; padding: {i}px; color: #{i % 10}{i % 10}{i % 10}; }}\n");
        }

        sb.Append("</style><script>");
        for (var i = 0; i < 600; i++)
        {
            sb.Append($"function handler{i}(event) {{ var value = event.target.value + {i}; return value * 2; }}\n");
        }

        sb.Append("</script></head><body><div id=\"__next\"><main>");
        sb.Append("<h1>Heavy page</h1><p>This is the one paragraph of real content on a very heavy page, long enough to count.</p>");
        for (var i = 0; i < 80; i++)
        {
            sb.Append($"<svg class=\"icon icon-{i}\" viewBox=\"0 0 24 24\" width=\"24\" height=\"24\"><path d=\"M12 2L2 7l10 5 10-5-10-5z M2 17l10 5 10-5 M2 12l10 5 10-5\" fill=\"none\" stroke=\"currentColor\"/></svg>");
            sb.Append($"<div class=\"wrapper wrapper-{i}\" data-index=\"{i}\" data-track=\"abc{i}\"><span class=\"label\" style=\"color: red\">x</span></div>");
        }

        sb.Append("</main></div></body></html>");
        return sb.ToString();
    }

    // A fake resolver: host name to addresses, or a failure for names it does not know.
    public sealed class FakeDns : IDnsResolver
    {
        private readonly Dictionary<string, IPAddress[]> _map = new(StringComparer.OrdinalIgnoreCase);

        // How many lookups were made, by host.
        public Dictionary<string, int> Calls { get; } = new(StringComparer.OrdinalIgnoreCase);

        // Adds a host with the addresses it should resolve to.
        public FakeDns Add(string host, params string[] addresses)
        {
            _map[host] = addresses.Select(IPAddress.Parse).ToArray();
            return this;
        }

        // Returns the configured addresses, or throws like a failed lookup.
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct)
        {
            Calls[host] = Calls.GetValueOrDefault(host) + 1;
            return _map.TryGetValue(host, out var addresses)
                ? Task.FromResult<IReadOnlyList<IPAddress>>(addresses)
                : throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound);
        }
    }
}
