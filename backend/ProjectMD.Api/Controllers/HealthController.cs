using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectMD.Api.Data;

namespace ProjectMD.Api.Controllers;

// Anonymous liveness check for uptime monitors and for the frontend to test the connection.
[ApiController]
[AllowAnonymous]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly IServiceProvider _services;

    // Receives the service provider so the database context is only created when ?db=true is used.
    public HealthController(IServiceProvider services)
    {
        _services = services;
    }

    // GET /api/health returns { status: "ok" }. With ?db=true it also checks that the database
    // answers: { status: "ok", database: "up" }, or 503 { status: "degraded", database: "down" }.
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] bool db = false, CancellationToken ct = default)
    {
        if (!db)
        {
            return Ok(new { status = "ok" });
        }

        var context = _services.GetRequiredService<AppDbContext>();
        if (await context.Database.CanConnectAsync(ct))
        {
            return Ok(new { status = "ok", database = "up" });
        }

        return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "degraded", database = "down" });
    }
}
