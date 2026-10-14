using System.Net;
using ProjectMD.Api.Services.Extraction;
using static ProjectMD.Api.Tests.Extraction.ExtractionTestSupport;

namespace ProjectMD.Api.Tests.Extraction;

// Tests for the SSRF request guard, using a fake DNS resolver (no network).
public class RequestGuardTests
{
    private static RequestGuard GuardFor(IDnsResolver dns, int maxRedirects = 8) => new(dns, maxRedirects);

    // A name that resolves only to public addresses is allowed.
    [Fact]
    public async Task Public_name_is_allowed()
    {
        var guard = GuardFor(new FakeDns().Add("example.com", "93.184.216.34", "2606:2800:220:1:248:1893:25c8:1946"));

        var decision = await guard.CheckAsync("https://example.com/path?x=1", 0, CancellationToken.None);

        Assert.True(decision.Allowed);
        Assert.Equal(GuardVerdict.Allowed, decision.Verdict);
    }

    // Names that resolve to private, loopback, link-local (cloud metadata) and other non-public
    // addresses are blocked.
    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.9.1")]
    [InlineData("192.168.1.10")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("169.254.169.254")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1234")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("224.0.0.1")]
    public async Task Names_resolving_to_non_public_addresses_are_blocked(string address)
    {
        var guard = GuardFor(new FakeDns().Add("evil.example.com", address));

        var decision = await guard.CheckAsync("https://evil.example.com/", 0, CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(GuardVerdict.BlockedAddress, decision.Verdict);
    }

    // One private address among public ones is enough to block (the browser might use it).
    [Fact]
    public async Task Mixed_public_and_private_records_are_blocked()
    {
        var guard = GuardFor(new FakeDns().Add("mixed.example.com", "93.184.216.34", "10.1.2.3"));

        var decision = await guard.CheckAsync("https://mixed.example.com/", 0, CancellationToken.None);

        Assert.Equal(GuardVerdict.BlockedAddress, decision.Verdict);
    }

    // Also when the private record is the IPv6 one.
    [Fact]
    public async Task Mixed_ipv4_public_and_ipv6_loopback_is_blocked()
    {
        var guard = GuardFor(new FakeDns().Add("mixed6.example.com", "93.184.216.34", "::1"));

        Assert.Equal(GuardVerdict.BlockedAddress, (await guard.CheckAsync("http://mixed6.example.com/", 0, CancellationToken.None)).Verdict);
    }

    // IP addresses written in the URL are judged directly, without any DNS lookup.
    [Theory]
    [InlineData("http://127.0.0.1/", false)]
    [InlineData("http://127.0.0.1:8080/admin", false)]
    [InlineData("http://[::1]/", false)]
    [InlineData("http://169.254.169.254/latest/meta-data/", false)]
    [InlineData("http://10.0.0.1/", false)]
    [InlineData("http://2130706433/", false)]
    [InlineData("http://0x7f.1/", false)]
    [InlineData("http://[::ffff:7f00:1]/", false)]
    [InlineData("http://93.184.216.34/", true)]
    [InlineData("https://[2606:2800:220:1:248:1893:25c8:1946]/", true)]
    public async Task Ip_literals_are_judged_without_dns(string url, bool allowed)
    {
        var dns = new FakeDns();
        var guard = GuardFor(dns);

        var decision = await guard.CheckAsync(url, 0, CancellationToken.None);

        Assert.Equal(allowed, decision.Allowed);
        Assert.Empty(dns.Calls);
    }

    // Only http and https are allowed.
    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/file")]
    [InlineData("data:text/html,<h1>x</h1>")]
    [InlineData("javascript:alert(1)")]
    [InlineData("chrome://settings")]
    [InlineData("blob:https://example.com/123")]
    [InlineData("gopher://example.com/")]
    [InlineData("ws://example.com/socket")]
    public async Task Non_http_schemes_are_blocked(string url)
    {
        var guard = GuardFor(new FakeDns().Add("example.com", "93.184.216.34"));

        var decision = await guard.CheckAsync(url, 0, CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Verdict, new[] { GuardVerdict.BlockedScheme, GuardVerdict.InvalidUrl });
    }

    // A name that does not resolve, resolves to nothing, or whose lookup throws is blocked.
    [Fact]
    public async Task Dns_failures_block_the_request()
    {
        var guard = GuardFor(new FakeDns().Add("empty.example.com"));

        Assert.Equal(GuardVerdict.DnsFailure, (await guard.CheckAsync("https://nothere.example.com/", 0, CancellationToken.None)).Verdict);
        Assert.Equal(GuardVerdict.DnsFailure, (await guard.CheckAsync("https://empty.example.com/", 0, CancellationToken.None)).Verdict);
    }

    // A resolver that throws something unexpected also means "blocked", not a crash.
    [Fact]
    public async Task Resolver_errors_fail_closed()
    {
        var guard = new RequestGuard(new ThrowingDns());

        var decision = await guard.CheckAsync("https://example.com/", 0, CancellationToken.None);

        Assert.Equal(GuardVerdict.DnsFailure, decision.Verdict);
    }

    // A hostile redirect: the first hop is public, the next hop points at an internal host. Each
    // hop is checked on its own, so the second is blocked while the first was allowed.
    [Fact]
    public async Task Redirect_to_a_private_host_is_blocked()
    {
        var dns = new FakeDns().Add("public.example.com", "93.184.216.34").Add("intranet.example.com", "10.20.30.40");
        var guard = GuardFor(dns);

        var first = await guard.CheckAsync("https://public.example.com/start", 0, CancellationToken.None);
        var second = await guard.CheckAsync("http://intranet.example.com/admin", 1, CancellationToken.None);
        var third = await guard.CheckAsync("http://169.254.169.254/latest/meta-data/", 2, CancellationToken.None);

        Assert.True(first.Allowed);
        Assert.Equal(GuardVerdict.BlockedAddress, second.Verdict);
        Assert.Equal(GuardVerdict.BlockedAddress, third.Verdict);
    }

    // Too many redirects are refused, even to a public host.
    [Fact]
    public async Task Redirect_chains_are_capped()
    {
        var guard = GuardFor(new FakeDns().Add("example.com", "93.184.216.34"), maxRedirects: 3);

        Assert.True((await guard.CheckAsync("https://example.com/", 3, CancellationToken.None)).Allowed);
        Assert.Equal(GuardVerdict.TooManyRedirects, (await guard.CheckAsync("https://example.com/", 4, CancellationToken.None)).Verdict);
    }

    // Host names are looked up once per guard, ignoring case and a trailing dot.
    [Fact]
    public async Task Decisions_are_cached_per_host()
    {
        var dns = new FakeDns().Add("example.com", "93.184.216.34");
        var guard = GuardFor(dns);

        await guard.CheckAsync("https://example.com/a", 0, CancellationToken.None);
        await guard.CheckAsync("https://EXAMPLE.com/b", 0, CancellationToken.None);
        await guard.CheckAsync("https://example.com./c", 0, CancellationToken.None);

        Assert.Equal(1, dns.Calls["example.com"]);
    }

    // The caller's cancellation is not swallowed as a DNS failure.
    [Fact]
    public async Task Cancellation_is_passed_through()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var guard = new RequestGuard(new CancelAwareDns());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guard.CheckAsync("https://example.com/", 0, cts.Token));
    }

    // Garbage that is not a URL is refused.
    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    public async Task Invalid_urls_are_blocked(string url)
    {
        var guard = GuardFor(new FakeDns());

        Assert.Equal(GuardVerdict.InvalidUrl, (await guard.CheckAsync(url, 0, CancellationToken.None)).Verdict);
    }

    // Ports that browsers refuse (mail, databases, admin services...) are blocked even on a public host.
    [Theory]
    [InlineData("http://example.com:25/", false)]
    [InlineData("http://example.com:22/", false)]
    [InlineData("https://example.com:6379/", false)]
    [InlineData("http://example.com:3306/", false)]
    [InlineData("http://example.com:6667/", false)]
    [InlineData("http://example.com:8080/", true)]
    [InlineData("https://example.com:8443/", true)]
    [InlineData("http://example.com:3000/", true)]
    [InlineData("https://example.com/", true)]
    public async Task Restricted_ports_are_blocked(string url, bool allowed)
    {
        var guard = GuardFor(new FakeDns().Add("example.com", "93.184.216.34"));

        var decision = await guard.CheckAsync(url, 0, CancellationToken.None);

        Assert.Equal(allowed, decision.Allowed);
        if (!allowed)
        {
            Assert.Equal(GuardVerdict.BlockedPort, decision.Verdict);
        }
    }

    // An allowed decision carries the checked addresses; those are what a connection must use.
    [Fact]
    public async Task Allowed_decisions_carry_the_checked_addresses()
    {
        var guard = GuardFor(new FakeDns().Add("example.com", "93.184.216.34", "2606:2800:220:1:248:1893:25c8:1946"));

        var decision = await guard.CheckAsync("https://example.com/", 0, CancellationToken.None);
        var literal = await guard.CheckAsync("https://93.184.216.34/", 0, CancellationToken.None);

        Assert.Equal([IPAddress.Parse("93.184.216.34"), IPAddress.Parse("2606:2800:220:1:248:1893:25c8:1946")], decision.Addresses);
        Assert.Equal([IPAddress.Parse("93.184.216.34")], literal.Addresses);
    }

    // The connect-time check (CheckHostAsync) uses the same remembered answer as the URL check, so a
    // DNS server that changes its answer later cannot redirect the connection (no DNS rebinding).
    [Fact]
    public async Task A_changed_dns_answer_is_never_used_after_the_check()
    {
        var dns = new RebindingDns();
        var guard = GuardFor(dns);

        var checkedUrl = await guard.CheckAsync("https://rebind.example.com/", 0, CancellationToken.None);
        var atConnect = await guard.CheckHostAsync("rebind.example.com", CancellationToken.None);

        Assert.True(checkedUrl.Allowed);
        Assert.True(atConnect.Allowed);
        Assert.Equal([IPAddress.Parse("93.184.216.34")], atConnect.Addresses);
        Assert.Equal(1, dns.Lookups);
    }

    // A fresh guard (the next page fetch) asks again, so the first answer is only kept for one fetch.
    [Fact]
    public async Task A_new_guard_asks_dns_again()
    {
        var dns = new RebindingDns();

        await GuardFor(dns).CheckAsync("https://rebind.example.com/", 0, CancellationToken.None);
        var second = await GuardFor(dns).CheckAsync("https://rebind.example.com/", 0, CancellationToken.None);

        Assert.Equal(GuardVerdict.BlockedAddress, second.Verdict);
        Assert.Equal(2, dns.Lookups);
    }

    // A resolver that answers with a public address first and a private one afterwards.
    private sealed class RebindingDns : IDnsResolver
    {
        public int Lookups { get; private set; }

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct)
        {
            Lookups++;
            return Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse(Lookups == 1 ? "93.184.216.34" : "10.0.0.9")]);
        }
    }

    // A resolver that throws a non-network exception.
    private sealed class ThrowingDns : IDnsResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }

    // A resolver that honours the cancellation token.
    private sealed class CancelAwareDns : IDnsResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("93.184.216.34")]);
        }
    }
}
