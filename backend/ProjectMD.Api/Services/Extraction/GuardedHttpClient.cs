using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ProjectMD.Api.Tests")]

namespace ProjectMD.Api.Services.Extraction;

// Thrown (inside the HTTP connect step) when the guard refuses a connection. HttpClient wraps it in
// an HttpRequestException; PageRequestProxy looks for it there.
public sealed class RequestBlockedException : Exception
{
    // Creates the exception for a refused decision.
    public RequestBlockedException(GuardDecision decision)
        : base("The request was blocked by the SSRF guard.")
    {
        Decision = decision;
    }

    // Why it was refused.
    public GuardDecision Decision { get; }
}

// The only HTTP client the page fetcher uses to reach the internet. It is built so that it cannot
// be pointed at a private network: whenever it opens a connection it asks the RequestGuard about
// the host and connects to the exact public IP addresses the guard checked (not to a fresh DNS
// answer), so a hostile DNS server cannot answer "public" for the check and "private" for the
// connection (DNS rebinding). Redirects are never followed here (the caller follows them one hop at
// a time and re-checks each), system proxies are ignored, certificates are validated normally, and
// cookies live in a container that belongs to this one client (one page fetch).
public sealed class GuardedHttpClient : IDisposable
{
    // How long opening one TCP connection may take.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    private readonly RequestGuard _guard;
    private readonly SocketsHttpHandler _handler;
    private readonly HttpClient _http;

    // Creates a client whose connections are all vetted by the guard.
    public GuardedHttpClient(RequestGuard guard)
    {
        _guard = guard;
        _handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            UseProxy = false,
            ConnectTimeout = ConnectTimeout,
            PooledConnectionLifetime = TimeSpan.FromSeconds(60),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(15),
            MaxConnectionsPerServer = 8,
            ConnectCallback = ConnectAsync,
        };
        _http = new HttpClient(_handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    // Sends one request and returns as soon as the response headers have arrived (the body is read
    // by the caller). Never follows a redirect.
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

    // Finds a guard refusal inside an exception (HttpClient wraps what the connect step throws).
    public static RequestBlockedException? FindBlocked(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is RequestBlockedException blocked)
            {
                return blocked;
            }
        }

        return null;
    }

    // Opens the TCP connection: asks the guard about the host, then connects to one of the
    // addresses it approved. TLS (for https) is layered on top by the handler afterwards.
    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var decision = await _guard.CheckHostAsync(host, ct);
        if (!decision.Allowed || decision.Addresses is null || decision.Addresses.Count == 0)
        {
            throw new RequestBlockedException(decision);
        }

        Exception? last = null;
        foreach (var address in decision.Addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                last = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException("Could not connect to any address of the host.", last);
    }

    // Disposes the connections.
    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }

    // Removes header names that belong to one connection or are set by the HTTP stack itself, so the
    // remaining headers can be copied between a browser request and ours (or a response and the
    // browser's fulfilled response).
    public static bool IsHopByHopOrManaged(string name) =>
        name.Equals("host", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("connection", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("keep-alive", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("proxy-connection", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("proxy-authenticate", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("proxy-authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("te", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("trailer", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("upgrade", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("content-length", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("accept-encoding", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("content-encoding", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("expect", StringComparison.OrdinalIgnoreCase);

    // Every header of a response, from both the response and the content, as name/value pairs.
    public static List<KeyValuePair<string, string>> AllHeaders(HttpResponseMessage response)
    {
        var all = new List<KeyValuePair<string, string>>();
        foreach (var header in response.Headers)
        {
            all.Add(new(header.Key, string.Join(header.Key.Equals("set-cookie", StringComparison.OrdinalIgnoreCase) ? "\n" : ", ", header.Value)));
        }

        foreach (var header in response.Content.Headers)
        {
            all.Add(new(header.Key, string.Join(", ", header.Value)));
        }

        return all;
    }

    // Reads the body, stopping after maxBytes. Returns the bytes and whether more was available.
    public static async Task<(byte[] Body, bool Truncated)> ReadBodyAsync(HttpResponseMessage response, long maxBytes, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (buffer.Length <= maxBytes)
        {
            var wanted = (int)Math.Min(chunk.Length, maxBytes + 1 - buffer.Length);
            var read = await stream.ReadAsync(chunk.AsMemory(0, wanted), ct);
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length > maxBytes)
        {
            return (buffer.GetBuffer().AsSpan(0, (int)maxBytes).ToArray(), true);
        }

        return (buffer.ToArray(), false);
    }

    // Adds a header to a request message, putting content headers on the content.
    public static void AddHeader(HttpRequestMessage request, string name, string value)
    {
        if (!request.Headers.TryAddWithoutValidation(name, value) && request.Content is not null)
        {
            request.Content.Headers.TryAddWithoutValidation(name, value);
        }
    }

    // The media type of a response's Content-Type (lower case, no parameters), or null.
    public static string? MediaType(HttpResponseMessage response) =>
        response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();

    // The declared length of a response body, if any.
    public static long? DeclaredLength(HttpResponseMessage response) => response.Content.Headers.ContentLength;

    // A new request message with an optional body (content type is added by AddHeader).
    public static HttpRequestMessage NewRequest(HttpMethod method, Uri url, byte[]? body)
    {
        var request = new HttpRequestMessage(method, url) { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
        if (body is { Length: > 0 } && method != HttpMethod.Get && method != HttpMethod.Head)
        {
            request.Content = new ByteArrayContent(body);
        }

        return request;
    }
}
