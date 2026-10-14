using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ProjectMD.Api.Auth;

// One call that registers everything authentication and authorization need. Program.cs calls it
// from the AUTH SERVICES block.
public static class AuthServiceCollectionExtensions
{
    // Registers Firebase ID token validation, one authorization policy per permission, a fallback
    // policy that requires a signed-in user, the 401/403 ProblemDetails handling, ICurrentUser and
    // IUserSync.
    public static IServiceCollection AddFirebaseAuth(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        // Authentication: bearer tokens only, validated by ConfigureFirebaseJwtBearer.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureFirebaseJwtBearer>();

        // Build the bearer options at startup so a missing Firebase:ProjectId stops the app
        // immediately instead of failing the first request.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).ValidateOnStart();

        // Authorization: the policy name IS the permission name, for example "scan:create".
        services.AddAuthorization(options =>
        {
            foreach (var permission in Permissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(permission)));
            }

            // Anything without an explicit [Authorize] or [AllowAnonymous] needs a signed-in user,
            // so a forgotten attribute can never expose a route.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();

        // 403 bodies carry the `reason` extension. Replace (not add) so ours is the one used.
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationResultHandler>());

        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IUserSync, UserSync>();

        return services;
    }
}
