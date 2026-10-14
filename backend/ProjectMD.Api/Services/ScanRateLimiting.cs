using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ProjectMD.Api.Services;

// Settings for the scan-creation rate limit, bound from "RateLimiting:ScanCreate".
public sealed class ScanRateLimitOptions
{
    // POST /api/scans requests allowed per user per minute (default 5).
    public int PermitsPerMinute { get; set; } = 5;
}

// The per-user rate limit for POST /api/scans, to stop a signed-in user from hammering the
// (later paid) scan generator. Over the limit the API answers 429 with a ProblemDetails body
// carrying reason "rate" and a Retry-After header.
public static class ScanRateLimiting
{
    // The policy name used by [EnableRateLimiting] on the create action.
    public const string PolicyName = "scan-create";

    // Registers the rate limiter service with the per-user fixed-window policy.
    public static IServiceCollection AddScanRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;

            // One window per user: keyed by the Firebase uid claim. Authorization has already run,
            // so the uid is present; the IP address is only a safety net.
            options.AddPolicy(PolicyName, httpContext =>
            {
                var permits = httpContext.RequestServices
                    .GetRequiredService<IOptions<ScanRateLimitOptions>>().Value.PermitsPerMinute;

                return RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, permits),
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });
        });

        return services;
    }

    // The key that decides whose bucket a request counts against: "uid:<firebase uid>".
    private static string PartitionKey(HttpContext httpContext)
    {
        var user = httpContext.User;
        var uid = user.FindFirst("sub")?.Value
            ?? user.FindFirst("user_id")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return uid is { Length: > 0 }
            ? "uid:" + uid
            : "ip:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }

    // Writes the 429 response: Retry-After plus a ProblemDetails body with reason "rate".
    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken ct)
    {
        var httpContext = context.HttpContext;
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            httpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests",
            Detail = "You are creating scans too quickly. Wait a moment and try again.",
        };
        problem.Extensions["reason"] = "rate";

        var written = await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>()
            .TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
        if (!written)
        {
            await httpContext.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", ct);
        }
    }
}
