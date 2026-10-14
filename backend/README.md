# Project MD backend

ASP.NET Core (.NET 10) Web API with EF Core 10 and PostgreSQL. The frontend talks to it over
JSON with a Firebase ID token as a bearer token.

```
backend/
  ProjectMD.sln
  .config/dotnet-tools.json   local tool: dotnet-ef (matches the EF Core major version)
  ProjectMD.Api/
    Program.cs                composition root (marked blocks for auth and scans)
    Models/                   AppUser, Scan, ScanTech
    Data/                     AppDbContext, design-time factory, Migrations/
    Dtos/                     ScanDto, TechDto, CreateScanRequest, UsageDto, MeDto
    Controllers/              HealthController (more are added by the auth and scans work)
    Auth/  Services/          reserved for the auth and scans work
```

## Run

Prerequisite: the .NET 10 SDK.

```powershell
cd backend
dotnet tool restore                # installs the pinned dotnet-ef
dotnet build
dotnet run --project ProjectMD.Api # listens on http://localhost:5080 in Development
```

The API refuses to start until a database connection string is configured (see below).

Check it is up: `GET http://localhost:5080/api/health` returns `{ "status": "ok" }`.
`GET /api/health?db=true` also checks the database and returns `503` if it cannot be reached.

## Configuration and secrets

`ProjectMD.Api/appsettings.json` is committed and holds no secrets:

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Default` | empty | PostgreSQL connection string (set it as a secret, see below) |
| `Firebase:ProjectId` | `project-md-51f14` | Used to validate Firebase ID tokens |
| `Cors:AllowedOrigins` | `["http://localhost:3000"]` | Browser origins allowed to call the API |
| `Usage:MonthlyLimit` | `10` | Scans per user per UTC calendar month |

Never put a real connection string, password or token in a tracked file, a script, this README
or a log.

### Connection string

Development: store it in [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
(kept outside the repo, per developer):

```powershell
dotnet user-secrets set "ConnectionStrings:Default" "<npgsql connection string>" --project ProjectMD.Api
```

Other environments: set the environment variable `ConnectionStrings__Default` (two underscores).
List values use indexes, for example `Cors__AllowedOrigins__0=https://app.example.com`.

Npgsql connection string format (placeholders only):

```
Host=<host>;Port=<port>;Database=<database>;Username=<user>;Password=<password>;SSL Mode=Require
```

Local overrides that must not be committed can go in `appsettings.Development.local.json`
(ignored by git) but user-secrets is preferred.

## Database migrations

The `dotnet ef` tool is pinned in `.config/dotnet-tools.json`. All commands run from `backend/`
and use `ProjectMD.Api` as both the project and the startup project:

```powershell
dotnet tool restore

# Create a new migration after changing the models
dotnet ef migrations add <Name> --project ProjectMD.Api --output-dir Data/Migrations

# Review the SQL before applying anything
dotnet ef migrations script --project ProjectMD.Api

# Apply to the database named by ConnectionStrings:Default (user-secrets / env var)
dotnet ef database update --project ProjectMD.Api
```

`InitialCreate` creates the `AppUsers` and `Scans` tables. It has to be reviewed and applied
deliberately; the API never migrates the database on startup.

### Generating a migration without a database

Creating a migration or a SQL script does not open a connection, but the design-time factory
(`Data/DesignTimeDbContextFactory.cs`) still needs some connection string. Give it a dummy local
value through an environment variable for that one command only:

```powershell
$env:ConnectionStrings__Default = "Host=localhost;Port=1;Database=dummy;Username=dummy;Password=not-a-real-password"
dotnet ef migrations add <Name> --project ProjectMD.Api --output-dir Data/Migrations
$env:ConnectionStrings__Default = $null
```

## Data model

- `AppUsers`: `Id` (guid, PK), `FirebaseUid` (unique, max 128), `Email` (max 320), `CreatedAt`,
  `LastSeenAt` (both `timestamptz`, UTC). Roles are not stored; the Firebase custom claim `role`
  is the source of truth.
- `Scans`: `Id`, `UserId` (FK to `AppUsers`, cascade delete), `Url` (max 2048), `Host` (max 255),
  `CreatedAt`, `DurationMs`, `Confidence` (0-100), `Pages`, `Tech` (`jsonb` array of
  `{ Name, Highlighted }`), `Markdown` (`text`). Index on `(UserId, CreatedAt DESC)`.

All timestamps are UTC: always write `DateTime.UtcNow`.

## API contract

JSON is camelCase. A scan is `{ id, url, host, createdAt, durationMs, confidence, pages,
tech: [{ name, highlighted }], markdown }` and matches `frontend/lib/types.ts`. Errors are
ProblemDetails JSON.

## Logging

Request logging records method, path (no query string), status code and duration only. Headers
are never logged, so the `Authorization` header cannot leak into logs.

## Authentication and roles

Every `/api/*` route except `/api/health` needs `Authorization: Bearer <Firebase ID token>`.
The token is validated with no service account: issuer `https://securetoken.google.com/<Firebase:ProjectId>`,
audience `<Firebase:ProjectId>`, RS256 only, lifetime checked with at most 60 s of clock skew.
Firebase's public keys are downloaded from the issuer's OIDC metadata. A missing or invalid token
is `401`; a valid token that lacks the permission is `403` ProblemDetails. For `scan:create` the
403 body has a `reason` field: `"unverified"` (verify the email) or `"role"` (the role cannot do
it). The same `reason` is sent for every refused permission. Tokens are never logged.

The role comes from the Firebase custom claim `role` (never from the database): no claim means
`member`, `viewer`/`member`/`admin` are used as is, and any other value is `viewer` (fail closed).
This is the same matrix as `frontend/app/functions/rbac`:

| Role | Permissions (policy names) |
|---|---|
| viewer | `scan:read`, `scan:export` |
| member | viewer + `scan:create`, `history:clear` |
| admin | member + `admin:access` |

`scan:create` also needs `email_verified == true` for every role. Controllers use
`[Authorize(Policy = Permissions.ScanCreate)]` (constants in `Auth/Permissions.cs`) and get the
caller from `ICurrentUser`; `IUserSync.GetOrCreateAsync` returns the caller's `AppUsers` row.
`GET /api/me` returns `{ uid, email, emailVerified, role, usage }`.

TODO(roles): assigning the `role` custom claim to users needs a Firebase service account (Admin
SDK) and is a later task. Until then every account has no claim and is therefore a `member`.
