using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ProjectMD.Api.Auth;
using ProjectMD.Api.Data;
using ProjectMD.Api.Dtos;

namespace ProjectMD.Api.Controllers;

// The body of PUT /api/admin/users/{id}/role.
public sealed record ChangeRoleRequest(string? Role);

// Admin-only user management: list users and change a user's role. Every action needs the
// admin:access permission (403 with reason "role" otherwise).
[ApiController]
[Authorize(Policy = Permissions.AdminAccess)]
[Route("api/admin/users")]
public class AdminController : ControllerBase
{
    // Most users returned by the list.
    private const int MaxUsers = 200;

    // How often a role change is retried when the database reports a serialization conflict.
    private const int MaxAttempts = 3;

    // Role changes run one at a time inside this process, on top of the serializable transaction
    // (which protects against other server instances).
    private static readonly SemaphoreSlim ChangeLock = new(1, 1);

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AdminController> _logger;

    // Receives the database, the signed-in admin, the role cache and a logger.
    public AdminController(AppDbContext db, ICurrentUser currentUser, IMemoryCache cache, ILogger<AdminController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _cache = cache;
        _logger = logger;
    }

    // GET /api/admin/users: up to 200 users, newest first.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminUserDto>>> List(CancellationToken cancellationToken)
    {
        var users = await _db.AppUsers
            .AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .Take(MaxUsers)
            .ToListAsync(cancellationToken);

        return users.Select(AdminUserDto.From).ToList();
    }

    // PUT /api/admin/users/{id}/role { role }: returns the updated user. 400 "role" for an unknown
    // role, 400 "self" for changing your own role, 409 "last-admin" when it would leave no admin,
    // 404 when the user does not exist.
    [HttpPut("{id:guid}/role")]
    public async Task<ActionResult<AdminUserDto>> ChangeRole(Guid id, [FromBody] ChangeRoleRequest request, CancellationToken cancellationToken)
    {
        if (!Roles.IsRole(request.Role))
        {
            return Reason(StatusCodes.Status400BadRequest, "role", "Invalid role", "The role must be viewer, member or admin.");
        }

        var newRole = request.Role!;

        await ChangeLock.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await TryChangeAsync(id, newRole, cancellationToken);
                }
                catch (Exception ex) when (attempt < MaxAttempts && IsSerializationFailure(ex))
                {
                    _db.ChangeTracker.Clear();
                }
            }
        }
        finally
        {
            ChangeLock.Release();
        }
    }

    // Does the checks and the update in one serializable transaction.
    private async Task<ActionResult<AdminUserDto>> TryChangeAsync(Guid id, string newRole, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var target = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (target is null)
        {
            return Reason(StatusCodes.Status404NotFound, "", "User not found", "There is no user with that id.");
        }

        if (target.FirebaseUid == _currentUser.Uid)
        {
            return Reason(StatusCodes.Status400BadRequest, "self", "Cannot change your own role", "You can't change your own role.");
        }

        var oldRole = Roles.FromStored(target.Role);
        if (oldRole == Roles.Admin && newRole != Roles.Admin)
        {
            var otherAdmins = await _db.AppUsers
                .CountAsync(u => u.Role == Roles.Admin && u.Id != target.Id, cancellationToken);
            if (otherAdmins == 0)
            {
                return Reason(StatusCodes.Status409Conflict, "last-admin", "Last admin", "There must be at least one admin.");
            }
        }

        if (target.Role != newRole)
        {
            target.Role = newRole;
            await _db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        // The new role must apply on the very next request, not after the cache expires.
        _cache.Remove(DbRoleClaimsTransformation.CacheKey(target.FirebaseUid));

        _logger.LogInformation(
            "Role changed by {ActorUid} for user {UserId}: {From} -> {To}",
            _currentUser.Uid, target.Id, oldRole, newRole);

        return AdminUserDto.From(target);
    }

    // A ProblemDetails response with a machine-readable `reason`.
    private ObjectResult Reason(int status, string reason, string title, string detail)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, statusCode: status, title: title, detail: detail);
        if (reason.Length > 0)
        {
            problem.Extensions["reason"] = reason;
        }

        return new ObjectResult(problem) { StatusCode = status };
    }

    // True for a database serialization/deadlock conflict (PostgreSQL 40001 / 40P01), anywhere in
    // the exception chain.
    private static bool IsSerializationFailure(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is DbException { SqlState: "40001" or "40P01" })
            {
                return true;
            }
        }

        return false;
    }
}
