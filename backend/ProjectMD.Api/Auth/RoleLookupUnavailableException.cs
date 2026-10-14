using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ProjectMD.Api.Auth;

// Thrown when the database cannot tell us a caller's role. The request is refused (503); no
// default role is ever assumed.
public sealed class RoleLookupUnavailableException : Exception
{
    // Wraps the underlying database error.
    public RoleLookupUnavailableException(Exception inner)
        : base("The user's role could not be read from the database.", inner)
    {
    }
}

// Turns RoleLookupUnavailableException into a 503 ProblemDetails (no internals in the body).
public sealed class RoleLookupUnavailableHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    // Receives the service that writes ProblemDetails bodies.
    public RoleLookupUnavailableHandler(IProblemDetailsService problemDetails)
    {
        _problemDetails = problemDetails;
    }

    // Handles only the role lookup failure; every other exception goes on to the default 500.
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not RoleLookupUnavailableException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Service unavailable",
                Detail = "The server could not check your access right now. Please try again shortly.",
            },
        });
    }
}
