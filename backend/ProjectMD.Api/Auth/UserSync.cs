using Microsoft.EntityFrameworkCore;
using ProjectMD.Api.Data;
using ProjectMD.Api.Models;

namespace ProjectMD.Api.Auth;

// The database-backed IUserSync: an upsert of the AppUser row keyed by the Firebase uid.
public sealed class UserSync : IUserSync
{
    // Longest email the AppUsers.Email column holds (see AppDbContext).
    private const int MaxEmailLength = 320;

    private readonly AppDbContext _db;

    // Receives the request's database context.
    public UserSync(AppDbContext db)
    {
        _db = db;
    }

    // Reads the row by uid, inserts it when missing, then bumps LastSeenAt. The unique index on
    // FirebaseUid is the guard against two requests inserting the same new user at once: the
    // insert that loses throws, and we then read the row the winner created instead of failing.
    public async Task<AppUser> GetOrCreateAsync(ICurrentUser user, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var email = Clip(user.Email);

        var row = await FindAsync(user.Uid, cancellationToken);
        if (row is null)
        {
            row = new AppUser
            {
                Id = Guid.NewGuid(),
                FirebaseUid = user.Uid,
                Email = email,
                CreatedAt = now,
                LastSeenAt = now,
            };
            _db.AppUsers.Add(row);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return row;
            }
            catch (DbUpdateException)
            {
                // Forget our failed insert, then look for the row someone else just created.
                _db.Entry(row).State = EntityState.Detached;
                row = await FindAsync(user.Uid, cancellationToken);
                if (row is null)
                {
                    // Not a duplicate after all (a real database error): let it surface.
                    throw;
                }
            }
        }

        if (email is not null)
        {
            row.Email = email;
        }

        row.LastSeenAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return row;
    }

    // Looks the user up by their Firebase uid (tracked, so the caller can update it).
    private Task<AppUser?> FindAsync(string uid, CancellationToken cancellationToken) =>
        _db.AppUsers.FirstOrDefaultAsync(u => u.FirebaseUid == uid, cancellationToken);

    // Cuts the email to the column length so a pathological value cannot fail the insert.
    private static string? Clip(string? email) =>
        email is null || email.Length <= MaxEmailLength ? email : email[..MaxEmailLength];
}
