using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ProjectMD.Api.Tests.Auth;

// The fake Firebase project the auth tests pretend to be.
public static class TestFirebase
{
    // The project id the test host is configured with (Firebase:ProjectId).
    public const string ProjectId = "test-project-id";

    // The issuer a valid token must carry.
    public const string Issuer = "https://securetoken.google.com/" + ProjectId;
}

// Creates signed ID tokens that look like Firebase's, using a throwaway RSA key generated for the
// test run. The test host is told to trust this key instead of downloading Google's keys, so no
// test touches the network and no real credential is involved.
public static class TestTokens
{
    // The key the test host trusts (stands in for Firebase's signing key).
    public static readonly RsaSecurityKey SigningKey = NewKey("test-key");

    // A different RSA key the host does NOT trust, for forged-token tests.
    public static readonly RsaSecurityKey UntrustedKey = NewKey("other-key");

    // Makes a fresh 2048-bit RSA key pair with the given key id.
    private static RsaSecurityKey NewKey(string keyId) => new(RSA.Create(2048)) { KeyId = keyId };

    // Builds one signed token. By default it is a valid, verified member token. `role` null means
    // the claim is left out; `extraClaims` are added as is (use it for arrays and odd values).
    public static string Create(
        string? uid = "uid-1",
        string? email = "person@example.com",
        bool? emailVerified = true,
        string? role = null,
        string? issuer = TestFirebase.Issuer,
        string? audience = TestFirebase.ProjectId,
        DateTime? notBefore = null,
        DateTime? expires = null,
        SecurityKey? key = null,
        string algorithm = SecurityAlgorithms.RsaSha256,
        IDictionary<string, object>? extraClaims = null)
    {
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object>();
        if (uid is not null)
        {
            claims["sub"] = uid;
            claims["user_id"] = uid;
        }

        if (email is not null)
        {
            claims["email"] = email;
        }

        if (emailVerified is not null)
        {
            claims["email_verified"] = emailVerified.Value;
        }

        if (role is not null)
        {
            claims["role"] = role;
        }

        if (extraClaims is not null)
        {
            foreach (var (name, value) in extraClaims)
            {
                claims[name] = value;
            }
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = notBefore ?? now.AddMinutes(-1),
            Expires = expires ?? now.AddHours(1),
            Claims = claims,
            SigningCredentials = new SigningCredentials(key ?? SigningKey, algorithm),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    // A token whose header says alg "none" and that has no signature at all.
    public static string Unsigned(string uid = "uid-1")
    {
        var header = Base64Url("""{"alg":"none","typ":"JWT"}""");
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var payload = Base64Url(
            $$"""{"iss":"{{TestFirebase.Issuer}}","aud":"{{TestFirebase.ProjectId}}","sub":"{{uid}}","exp":{{exp}}}""");
        return $"{header}.{payload}.";
    }

    // Base64url-encodes text (the JWT segment encoding).
    private static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
