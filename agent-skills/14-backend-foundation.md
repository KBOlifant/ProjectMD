---
name: backend-foundation
description: Scaffold the Project MD ASP.NET Core backend: solution, project, EF Core PostgreSQL DbContext, models, DTOs, configuration and the first migration.
---

# Backend foundation

**Owns:** everything under `backend/` except `Auth/*`, `Controllers/MeController.cs` (15) and `Controllers/ScansController.cs`, `Services/*`, `*.Tests` (16).
**Stack:** .NET 10 SDK (installed), ASP.NET Core controllers, EF Core 10 with `Npgsql.EntityFrameworkCore.PostgreSQL` (PostgreSQL 18 on Aiven), `Microsoft.AspNetCore.Authentication.JwtBearer`. Check NuGet for the matching 10.x versions; do not pin to older majors.

## Layout
```
backend/
  ProjectMD.sln
  .gitignore                 # bin/ obj/ *.user, appsettings.*.local.json
  .config/dotnet-tools.json  # local tool: dotnet-ef (matching EF major)
  README.md                  # how to run, secrets, migrations
  ProjectMD.Api/
    ProjectMD.Api.csproj     # UserSecretsId set (dotnet user-secrets init)
    Program.cs
    appsettings.json         # NO secrets (see Configuration)
    Models/    AppUser.cs, Scan.cs, ScanTech.cs
    Data/      AppDbContext.cs, Migrations/
    Dtos/      ScanDto.cs, TechDto.cs, CreateScanRequest.cs, MeDto.cs, UsageDto.cs
    Controllers/HealthController.cs
```
Also create the empty folders `Auth/` and `Services/` with a `.gitkeep` so 15 and 16 can add files without touching yours.

## Models
- `AppUser`: `Guid Id` (PK), `string FirebaseUid` (unique index, max 128), `string? Email` (max 320), `DateTime CreatedAt`, `DateTime LastSeenAt` (UTC, `timestamptz`). Roles are NOT stored: the Firebase custom claim is the source of truth.
- `Scan`: `Guid Id`, `Guid UserId` (FK -> AppUser, cascade delete), `string Url` (max 2048), `string Host` (max 255), `DateTime CreatedAt`, `int DurationMs`, `int Confidence` (0-100), `int Pages`, `List<ScanTech> Tech`, `string Markdown`. Index on `(UserId, CreatedAt DESC)`.
- `ScanTech`: `string Name`, `bool Highlighted`. Mapped as an owned collection stored as `jsonb` on `Scan` (`OwnsMany(...).ToJson()`).
- Enable nullable reference types. All timestamps are UTC.

## DTOs (the JSON contract the frontend already uses; camelCase)
`ScanDto { id: string, url, host, createdAt: ISO-8601 string, durationMs, confidence, pages, tech: [{ name, highlighted }], markdown }` (matches `frontend/lib/types.ts`). `CreateScanRequest { url }`. `UsageDto { used, limit, resetsAt }`. `MeDto { uid, email, emailVerified, role, usage }`. Use `record` types and a small `ToDto()` mapping extension. Use `System.Text.Json` defaults (camelCase).

## Configuration
- `appsettings.json` (committed, no secrets): `Firebase:ProjectId = "project-md-51f14"`, `Cors:AllowedOrigins = ["http://localhost:3000"]`, `Usage:MonthlyLimit = 10`, `ConnectionStrings:Default = ""`.
- The connection string comes from **user-secrets** in development and from the environment variable `ConnectionStrings__Default` elsewhere. NEVER write a real connection string, password or token into any tracked file, the README, a script or a log. The README documents the secret's name and the Npgsql format only (`Host=...;Port=...;Database=...;Username=...;Password=...;SSL Mode=Require`), with placeholders.
- `Program.cs`: `AddDbContext<AppDbContext>(UseNpgsql(...))`, `AddControllers()`, CORS policy from config (explicit origins, `AllowAnyHeader`, `AllowAnyMethod`, no credentials), ProblemDetails for errors (`AddProblemDetails`, `UseExceptionHandler`), `UseHttpsRedirection` off in Development, request logging that never logs the `Authorization` header. Leave clearly marked extension points (comments) where 15 registers authentication/authorization and 16 registers services; they will edit only those marked blocks.
- Fail fast at startup with one clear message if `ConnectionStrings:Default` is empty (except when running `dotnet ef` design-time; add an `IDesignTimeDbContextFactory` that reads the same configuration so migrations work).
- Listen on `http://localhost:5080` in Development (`launchSettings.json`).

## Migrations
Create `InitialCreate` with the local `dotnet ef` tool. **Do not apply it** (the user's assistant applies it to the real database after review) and do not connect to any real database. Generating a migration does not need a live connection when the design-time factory is given a dummy local connection string through an environment variable for that command only.

## Rules
- Short plain-English comment above each class/method, matching the repo's comment style.
- `HealthController`: `GET /api/health` anonymous, returns `{ status: "ok" }` and, with `?db=true`, also checks `Database.CanConnectAsync()`.
- No Groq/LLM code, no Firebase Admin SDK, no secrets.

## Done when
`dotnet build` succeeds with 0 warnings you introduced, the migration exists and its SQL (`dotnet ef migrations script`) shows the `AppUsers` and `Scans` tables with the indexes above, and the README explains running and migrations.
