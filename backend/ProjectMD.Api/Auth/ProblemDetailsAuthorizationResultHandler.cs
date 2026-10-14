using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;

namespace ProjectMD.Api.Auth;

// Decides what the response looks like when authorization stops a request. A missing or invalid
// token stays a 401 challenge (the default handler, which also sets WWW-Authenticate; the
// status-code-pages middleware then gives it a ProblemDetails body). A failed permission becomes a
// 403 ProblemDetails with a `reason` extension ("role" or "unverified") for the frontend.
public sealed class ProblemDetailsAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();
    private readonly IProblemDetailsService _problemDetails;

    // Receives the service that writes ProblemDetails bodies (registered by AddProblemDetails).
    public ProblemDetailsAuthorizationResultHandler(IProblemDetailsService problemDetails)
    {
        _problemDetails = problemDetails;
    }

    // Writes the 403 body for a refused permission and hands everything else to the default handler.
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Forbidden)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Forbidden",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
            Detail = "You do not have permission to do this.",
        };

        var reason = ReasonFrom(authorizeResult);
        if (reason is not null)
        {
            problem.Extensions["reason"] = reason;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        var written = await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
        });

        if (!written)
        {
            await context.Response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, "application/problem+json");
        }
    }

    // Picks the "role" or "unverified" reason recorded by PermissionHandler, or null when the
    // refusal came from somewhere else (for example a plain [Authorize] with another requirement).
    // A role refusal wins if both were recorded.
    private static string? ReasonFrom(PolicyAuthorizationResult result)
    {
        var reasons = result.AuthorizationFailure?.FailureReasons
            .Where(r => r.Handler is PermissionHandler)
            .Select(r => r.Message)
            .ToList();

        if (reasons is null || reasons.Count == 0)
        {
            return null;
        }

        return reasons.Contains(DenyReasons.Role) ? DenyReasons.Role
            : reasons.Contains(DenyReasons.Unverified) ? DenyReasons.Unverified
            : null;
    }
}
