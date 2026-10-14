using ProjectMD.Api.Services;

namespace ProjectMD.Api.Tests.Scans;

// Tests for the scan URL validator: what it accepts, how it normalises, and every class of URL it
// must refuse (the security boundary for what the server may later fetch).
public class UrlValidatorTests
{
    private readonly UrlValidator _validator = new();

    // Valid URLs come back normalised, with the display host (no "www.").
    [Theory]
    [InlineData("example.com", "https://example.com/", "example.com")]
    [InlineData("  https://example.com  ", "https://example.com/", "example.com")]
    [InlineData("HTTP://Example.COM", "http://example.com/", "example.com")]
    [InlineData("https://www.example.com/docs?x=1", "https://www.example.com/docs?x=1", "example.com")]
    [InlineData("www.example.com", "https://www.example.com/", "example.com")]
    [InlineData("example.com:8080/path", "https://example.com:8080/path", "example.com")]
    [InlineData("https://example.com:443/a", "https://example.com/a", "example.com")]
    [InlineData("https://sub.domain.example.co.uk/a/b#frag", "https://sub.domain.example.co.uk/a/b#frag", "sub.domain.example.co.uk")]
    [InlineData("https://my_site.example.com", "https://my_site.example.com/", "my_site.example.com")]
    [InlineData("https://example.com./", "https://example.com./", "example.com")]
    [InlineData("https://example.com/a b", "https://example.com/a%20b", "example.com")]
    public void Valid_urls_are_accepted_and_normalised(string input, string expectedUrl, string expectedHost)
    {
        var result = _validator.Validate(input);

        Assert.True(result.IsValid, result.Error);
        Assert.Null(result.Error);
        Assert.Equal(expectedUrl, result.Url);
        Assert.Equal(expectedHost, result.Host);
        Assert.Equal(expectedUrl, result.Uri!.AbsoluteUri);
    }

    // The scheme is added when the user typed none, and an explicit http scheme is kept.
    [Fact]
    public void Scheme_is_added_when_missing_and_kept_when_given()
    {
        Assert.StartsWith("https://", _validator.Validate("example.com/docs").Url);
        Assert.StartsWith("http://", _validator.Validate("http://example.com/docs").Url);
        Assert.StartsWith("https://", _validator.Validate("HTTPS://example.com/docs").Url);
    }

    // International host names are stored as plain ASCII (punycode), like the browser's URL does.
    [Fact]
    public void International_host_is_stored_as_punycode()
    {
        // "bücher.de/ä" is bücher.de/ä (escapes keep this file pure ASCII).
        var result = _validator.Validate("https://bücher.de/ä");

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("xn--bcher-kva.de", result.Host);
        Assert.Equal("https://xn--bcher-kva.de/%C3%A4", result.Url);
        Assert.True(result.Url!.All(char.IsAscii));
    }

    // Public IP addresses are fine (only non-public ranges are refused).
    [Theory]
    [InlineData("https://8.8.8.8/", "8.8.8.8")]
    [InlineData("https://172.15.255.255/", "172.15.255.255")]
    [InlineData("https://172.32.0.1/", "172.32.0.1")]
    [InlineData("https://100.63.255.255/", "100.63.255.255")]
    [InlineData("https://100.128.0.1/", "100.128.0.1")]
    [InlineData("https://11.0.0.1/", "11.0.0.1")]
    [InlineData("https://[2606:4700:4700::1111]/", "[2606:4700:4700::1111]")]
    public void Public_ip_addresses_are_accepted(string input, string expectedHost)
    {
        var result = _validator.Validate(input);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(expectedHost, result.Host);
    }

    // An IPv4-mapped IPv6 form of a public address is judged by the address inside.
    [Fact]
    public void Ipv4_mapped_public_address_is_accepted()
    {
        Assert.True(_validator.Validate("https://[::ffff:8.8.8.8]/").IsValid);
    }

    // Empty, whitespace or missing input gets the "enter a URL" message.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \n")]
    public void Empty_input_is_rejected(string? input)
    {
        var result = _validator.Validate(input);

        Assert.False(result.IsValid);
        Assert.Equal(UrlValidator.EmptyMessage, result.Error);
        Assert.Null(result.Url);
    }

    // Anything but http and https is refused, including dangerous schemes that have "://".
    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://example.com")]
    [InlineData("javascript://example.com/%0Aalert(1)")]
    [InlineData("ws://example.com")]
    public void Other_schemes_are_rejected(string input)
    {
        var result = _validator.Validate(input);

        Assert.False(result.IsValid);
        Assert.Equal(UrlValidator.SchemeMessage, result.Error);
    }

    // Text that is not a URL, or is malformed, gets the "not a valid URL" message.
    [Theory]
    [InlineData("not a url")]
    [InlineData("https://")]
    [InlineData("http://exa mple.com")]
    [InlineData("https://example..com")]
    [InlineData("https://-a.com")]
    [InlineData("https://example.com:99999")]
    [InlineData("https://[::1")]
    [InlineData("https://exa\nmple.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("//example.com")]
    [InlineData("https://exa$mple.com")]
    [InlineData("https://256.1.1.1")]
    [InlineData("https://1.2.3.4.5")]
    [InlineData("https://foo.0x7f")]
    public void Malformed_urls_are_rejected(string input)
    {
        var result = _validator.Validate(input);

        Assert.False(result.IsValid);
        Assert.Equal(UrlValidator.InvalidMessage, result.Error);
    }

    // A username or password in the URL is refused (it could smuggle a different host or leak a secret).
    [Theory]
    [InlineData("https://user:pass@example.com")]
    [InlineData("https://user@example.com")]
    [InlineData("user:pass@example.com")]
    [InlineData("https://:@example.com")]
    [InlineData("mailto:someone@example.com")]
    public void Credentials_in_the_url_are_rejected(string input)
    {
        var result = _validator.Validate(input);

        Assert.False(result.IsValid);
        Assert.Equal(UrlValidator.CredentialsMessage, result.Error);
    }

    // The 2048 character limit: exactly 2048 passes, 2049 does not.
    [Fact]
    public void Length_limit_is_2048_characters()
    {
        const string prefix = "https://example.com/";
        var atLimit = prefix + new string('a', UrlValidator.MaxUrlLength - prefix.Length);
        var overLimit = atLimit + "a";

        Assert.True(_validator.Validate(atLimit).IsValid);

        var result = _validator.Validate(overLimit);
        Assert.False(result.IsValid);
        Assert.Equal(UrlValidator.TooLongMessage, result.Error);
    }

    // A URL that becomes longer than 2048 once normalised (the scheme is added) is still refused.
    [Fact]
    public void Length_limit_applies_after_the_scheme_is_added()
    {
        var input = "example.com/" + new string('a', UrlValidator.MaxUrlLength - "example.com/".Length);
        Assert.Equal(UrlValidator.MaxUrlLength, input.Length);

        var result = _validator.Validate(input);

        Assert.False(result.IsValid);
        Assert.Equal(UrlValidator.TooLongMessage, result.Error);
    }

    // Local-only host names and names without a dot are refused.
    [Theory]
    [InlineData("localhost")]
    [InlineData("http://localhost:3000")]
    [InlineData("https://LOCALHOST/")]
    [InlineData("https://localhost./")]
    [InlineData("https://foo.localhost")]
    [InlineData("https://printer.local")]
    [InlineData("https://printer.local.")]
    [InlineData("https://www.local")]
    [InlineData("https://db.internal")]
    [InlineData("https://metadata.google.internal/computeMetadata/v1/")]
    [InlineData("https://app.localdomain")]
    [InlineData("https://nas.lan")]
    [InlineData("https://wiki.intranet")]
    [InlineData("https://router.home.arpa")]
    [InlineData("http://example")]
    [InlineData("http://intranet")]
    [InlineData("https://www.com")]
    public void Local_and_dotless_hosts_are_rejected(string input)
    {
        var result = _validator.Validate(input);

        Assert.False(result.IsValid);
        Assert.Equal(UrlValidator.PrivateMessage, result.Error);
    }

    // IPv4 addresses that are loopback, private, link-local (cloud metadata), CGNAT, unspecified,
    // multicast, reserved, benchmarking or documentation are refused.
    [Theory]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://127.255.255.254:8080/admin")]
    [InlineData("http://10.0.0.1")]
    [InlineData("http://10.255.255.255")]
    [InlineData("http://172.16.0.1")]
    [InlineData("http://172.31.255.255")]
    [InlineData("http://192.168.0.1")]
    [InlineData("http://192.168.255.255")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://169.254.0.1")]
    [InlineData("http://100.64.0.1")]
    [InlineData("http://100.127.255.255")]
    [InlineData("http://0.0.0.0")]
    [InlineData("http://0.1.2.3")]
    [InlineData("http://224.0.0.1")]
    [InlineData("http://240.0.0.1")]
    [InlineData("http://255.255.255.255")]
    [InlineData("http://192.0.0.1")]
    [InlineData("http://192.0.2.1")]
    [InlineData("http://198.18.0.1")]
    [InlineData("http://198.19.255.255")]
    [InlineData("http://198.51.100.7")]
    [InlineData("http://203.0.113.7")]
    public void Non_public_ipv4_addresses_are_rejected(string input)
    {
        var result = _validator.Validate(input);

        Assert.False(result.IsValid);
        Assert.Equal(UrlValidator.PrivateMessage, result.Error);
    }

    // Other spellings of 127.0.0.1 (decimal, hex, octal, short forms) are still caught.
    [Theory]
    [InlineData("http://2130706433")]
    [InlineData("http://0x7f000001")]
    [InlineData("http://0x7f.0.0.1")]
    [InlineData("http://0177.0.0.1")]
    [InlineData("http://127.1")]
    [InlineData("http://017700000001")]
    [InlineData("http://2852039166")]
    public void Obfuscated_ipv4_spellings_are_rejected(string input)
    {
        var result = _validator.Validate(input);

        Assert.False(result.IsValid);
        Assert.Equal(UrlValidator.PrivateMessage, result.Error);
    }

    // IPv6 loopback, unspecified, unique local (fc00::/7), link-local (fe80::/10), site-local,
    // multicast, documentation and Teredo are refused.
    [Theory]
    [InlineData("http://[::1]")]
    [InlineData("http://[::1]:3000/")]
    [InlineData("http://[0:0:0:0:0:0:0:1]")]
    [InlineData("http://[::]")]
    [InlineData("http://[fc00::1]")]
    [InlineData("http://[fd12:3456:789a::1]")]
    [InlineData("http://[fe80::1]")]
    [InlineData("http://[febf::1]")]
    [InlineData("http://[fe80::1%25eth0]")]
    [InlineData("http://[fec0::1]")]
    [InlineData("http://[ff02::1]")]
    [InlineData("http://[2001:db8::1]")]
    [InlineData("http://[2001::1]")]
    [InlineData("http://[3fff::1]")]
    public void Non_public_ipv6_addresses_are_rejected(string input)
    {
        var result = _validator.Validate(input);

        Assert.False(result.IsValid);
        Assert.Equal(UrlValidator.PrivateMessage, result.Error);
    }

    // IPv6 forms that wrap an IPv4 address are judged by the IPv4 address inside:
    // IPv4-mapped, the deprecated IPv4-compatible form, NAT64 and 6to4.
    [Theory]
    [InlineData("http://[::ffff:127.0.0.1]")]
    [InlineData("http://[::ffff:7f00:1]")]
    [InlineData("http://[::ffff:10.0.0.1]")]
    [InlineData("http://[::ffff:192.168.1.1]")]
    [InlineData("http://[::ffff:169.254.169.254]")]
    [InlineData("http://[::ffff:a9fe:a9fe]")]
    [InlineData("http://[::127.0.0.1]")]
    [InlineData("http://[64:ff9b::7f00:1]")]
    [InlineData("http://[64:ff9b::a9fe:a9fe]")]
    [InlineData("http://[2002:7f00:1::]")]
    [InlineData("http://[2002:a9fe:a9fe::1]")]
    public void Ipv6_forms_wrapping_private_ipv4_are_rejected(string input)
    {
        var result = _validator.Validate(input);

        Assert.False(result.IsValid);
        Assert.Equal(UrlValidator.PrivateMessage, result.Error);
    }

    // The public-address helper agrees with the rules above for a few direct cases.
    [Fact]
    public void IsPublicAddress_classifies_addresses()
    {
        Assert.True(UrlValidator.IsPublicAddress(System.Net.IPAddress.Parse("1.1.1.1")));
        Assert.True(UrlValidator.IsPublicAddress(System.Net.IPAddress.Parse("2606:4700:4700::1111")));
        Assert.False(UrlValidator.IsPublicAddress(System.Net.IPAddress.Parse("10.1.2.3")));
        Assert.False(UrlValidator.IsPublicAddress(System.Net.IPAddress.Parse("::1")));
        Assert.False(UrlValidator.IsPublicAddress(System.Net.IPAddress.Parse("::ffff:10.1.2.3")));
    }
}
