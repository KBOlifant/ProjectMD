using System.Reflection;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.EntityFrameworkCore;
using ProjectMD.Api.Data;

// Entry point for the Project MD API. Skills 15 (auth) and 16 (scans) each edit ONLY their own
// marked blocks below; everything else here belongs to skill 14 (backend foundation).

var builder = WebApplication.CreateBuilder(args);

// True when the `dotnet ef` tools load this assembly (migrations). Startup checks are skipped then.
var isEfTooling = Assembly.GetEntryAssembly()?.GetName().Name == "ef";

// The test host uses the "Testing" environment: it brings its own database registration
// (SQLite or in-memory) and does not need a real connection string.
var isTesting = builder.Environment.IsEnvironment("Testing");

// Private settings (database address, admin emails) are read from backend/.env, which git
// ignores. Real environment variables still win. Tests never read the file.
if (!isTesting)
{
    builder.Configuration.AddInMemoryCollection(
        DotEnv.Read(DotEnv.DefaultFolders(builder.Environment.ContentRootPath)));
}

// ---- Database ---------------------------------------------------------------------------------
// The connection string comes from backend/.env (or user-secrets in development, or the
// ConnectionStrings__Default environment variable). It is never stored in the repo.
if (!isTesting)
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
}

// ---- Core services ----------------------------------------------------------------------------
builder.Services.AddControllers();

// Turns errors (including empty 401/403/404 and unhandled exceptions) into ProblemDetails JSON.
builder.Services.AddProblemDetails();

// Request logging: method, path (no query string), status code and duration only. Headers are
// never logged, so the Authorization header (the Firebase token) cannot end up in the logs.
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields =
        HttpLoggingFields.RequestMethod |
        HttpLoggingFields.RequestPath |
        HttpLoggingFields.ResponseStatusCode |
        HttpLoggingFields.Duration;
    options.CombineLogs = true;
});

// CORS: only the origins listed in config (Cors:AllowedOrigins), any header, any method, and no
// credentials (the API uses a bearer token, not cookies).
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// ===== BEGIN AUTH SERVICES (owned by skill 15; edit only inside this block) =====
// Registers Firebase ID token validation (JWT bearer), one authorization policy per permission
// plus a fallback policy that requires a signed-in user, the 401/403 ProblemDetails handling
// (403 carries `reason`), ICurrentUser and IUserSync. See Auth/AuthServiceCollectionExtensions.cs.
ProjectMD.Api.Auth.AuthServiceCollectionExtensions.AddFirebaseAuth(builder.Services);
// ===== END AUTH SERVICES =====

// ===== BEGIN SCAN SERVICES (owned by skill 16; edit only inside this block) =====
// Registers the clock, the URL validator, IScanGenerator -> StaticScanGenerator (MOCK data),
// IUsageService (monthly quota), the quota-checked scan recorder, their settings
// (Usage:MonthlyLimit, RateLimiting:ScanCreate:PermitsPerMinute) and the per-user rate limit
// policy for POST /api/scans. See Services/ScanServiceCollectionExtensions.cs.
ProjectMD.Api.Services.ScanServiceCollectionExtensions.AddScanServices(builder.Services, builder.Configuration);
// ===== END SCAN SERVICES =====

var app = builder.Build();

// ---- Startup checks ---------------------------------------------------------------------------
// Fail fast with one clear message if the database connection string is missing.
if (!isTesting && !isEfTooling && string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("Default")))
{
    Console.Error.WriteLine(
        "ConnectionStrings:Default is not set. Put ConnectionStrings__Default=<npgsql connection " +
        "string> in backend/.env (see backend/.env.example), or set it as an environment " +
        "variable. See backend/README.md.");
    Environment.Exit(1);
}

// ---- Request pipeline (order matters) ---------------------------------------------------------
// Catches unhandled exceptions and returns a ProblemDetails 500 without leaking internals.
app.UseExceptionHandler();

// Gives empty 4xx responses (for example 401 and 403) a ProblemDetails body.
app.UseStatusCodePages();

// HTTPS redirection is off in Development (the dev server only listens on http://localhost:5080).
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseHttpLogging();

app.UseRouting();

app.UseCors("Frontend");

// ===== BEGIN AUTH MIDDLEWARE (owned by skill 15; edit only inside this block) =====
// Reads the bearer token first, then enforces the policies. Both must come after UseRouting
// and UseCors, and before anything that needs to know who the caller is.
app.UseAuthentication();
app.UseAuthorization();
// ===== END AUTH MIDDLEWARE =====

// ===== BEGIN SCAN MIDDLEWARE (owned by skill 16; edit only inside this block) =====
// Applies the per-user rate limit to POST /api/scans. It runs after authentication and
// authorization so it can key on the signed-in user's uid, and so rejected sign-ins use no permits.
app.UseRateLimiter();
// ===== END SCAN MIDDLEWARE =====

app.MapControllers();

app.Run();

// Makes the entry point visible to WebApplicationFactory<Program> in the test project.
public partial class Program;
