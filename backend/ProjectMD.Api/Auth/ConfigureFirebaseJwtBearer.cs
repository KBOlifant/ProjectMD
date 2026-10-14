using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ProjectMD.Api.Auth;

// Sets up JWT bearer validation for Firebase ID tokens. No service account is needed: Firebase
// publishes its public signing keys, and the handler downloads them from the authority's OIDC
// metadata (https://securetoken.google.com/{projectId}/.well-known/openid-configuration).
// Reads Firebase:ProjectId when the options are first built, so test hosts can override it.
public sealed class ConfigureFirebaseJwtBearer : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly IConfiguration _configuration;

    // Receives the application configuration (Firebase:ProjectId).
    public ConfigureFirebaseJwtBearer(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // Applies the Firebase rules to the default "Bearer" scheme only.
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        var projectId = _configuration["Firebase:ProjectId"];
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new InvalidOperationException(
                "Firebase:ProjectId is not set. It is the Firebase project id used to validate ID tokens; see backend/README.md.");
        }

        var issuer = $"https://securetoken.google.com/{projectId}";

        // Keep the raw claim names from the token (sub, email, email_verified, role).
        options.MapInboundClaims = false;

        // Never keep the token in the authentication properties, so it cannot leak from there.
        options.SaveToken = false;

        // Signing keys come from this authority's OIDC metadata (HTTPS only).
        options.Authority = issuer;
        options.RequireHttpsMetadata = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = projectId,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(60),
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            NameClaimType = FirebaseClaims.Subject,
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = RejectTokensWithoutUid,
        };
    }

    // Used when the options are requested without a name; same rules as the default scheme.
    public void Configure(JwtBearerOptions options) => Configure(JwtBearerDefaults.AuthenticationScheme, options);

    // A signed token must still name a user. Without a uid (`sub` or `user_id`) the request is
    // not authenticated, so nothing downstream ever sees an anonymous "authenticated" caller.
    private static Task RejectTokensWithoutUid(TokenValidatedContext context)
    {
        if (FirebaseClaims.Read(context.Principal) is null)
        {
            context.Fail("The token does not identify a user.");
        }

        return Task.CompletedTask;
    }
}
