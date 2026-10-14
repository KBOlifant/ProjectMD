using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProjectMD.Api.Auth;
using ProjectMD.Api.Data;
using ProjectMD.Api.Models;

namespace ProjectMD.Api.Tests.Auth;

// Tests the AppUser upsert (IUserSync) on an in-memory SQLite database. Never a real database.
public class UserSyncTests : IDisposable
{
    private readonly SqliteTestDb _db = new();

    public void Dispose() => _db.Dispose();

    // A caller with the given uid and email.
    private static CurrentUser Caller(string uid = "uid-1", string? email = "a@example.com") =>
        new(uid, email, true, Roles.Member);

    [Fact]
    // First call creates the row with UTC timestamps.
    public async Task Creates_the_user_on_first_use()
    {
        await using var context = _db.CreateContext();
        var before = DateTime.UtcNow;

        var user = await new UserSync(context).GetOrCreateAsync(Caller());

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("uid-1", user.FirebaseUid);
        Assert.Equal("a@example.com", user.Email);
        Assert.True(user.CreatedAt >= before.AddSeconds(-1));
        Assert.True(user.LastSeenAt >= before.AddSeconds(-1));
        Assert.Equal(1, await context.AppUsers.CountAsync());
    }

    [Fact]
    // Later calls (even from a new request/context) reuse the row and refresh email and LastSeenAt.
    public async Task Updates_the_existing_user()
    {
        Guid id;
        DateTime firstSeen;
        await using (var first = _db.CreateContext())
        {
            var created = await new UserSync(first).GetOrCreateAsync(Caller(email: "old@example.com"));
            id = created.Id;
            firstSeen = created.LastSeenAt;
        }

        await Task.Delay(50);

        await using var second = _db.CreateContext();
        var again = await new UserSync(second).GetOrCreateAsync(Caller(email: "new@example.com"));

        Assert.Equal(id, again.Id);
        Assert.Equal("new@example.com", again.Email);
        Assert.True(again.LastSeenAt > firstSeen);
        Assert.Equal(1, await second.AppUsers.CountAsync());
    }

    [Fact]
    // A token without an email does not wipe the stored one.
    public async Task Keeps_the_stored_email_when_the_token_has_none()
    {
        await using var context = _db.CreateContext();
        var sync = new UserSync(context);
        await sync.GetOrCreateAsync(Caller(email: "kept@example.com"));

        var user = await sync.GetOrCreateAsync(Caller(email: null));

        Assert.Equal("kept@example.com", user.Email);
    }

    [Fact]
    // Different Firebase uids are different people.
    public async Task Different_uids_get_different_rows()
    {
        await using var context = _db.CreateContext();
        var sync = new UserSync(context);

        var a = await sync.GetOrCreateAsync(Caller("uid-a"));
        var b = await sync.GetOrCreateAsync(Caller("uid-b"));

        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(2, await context.AppUsers.CountAsync());
    }

    [Fact]
    // Two requests creating the same new user at once: the unique index rejects the second insert
    // and it recovers by reading the first request's row instead of failing.
    public async Task Survives_a_concurrent_insert_of_the_same_uid()
    {
        var rivalId = Guid.NewGuid();
        var interceptor = new RaceInterceptor(async () =>
        {
            await using var rival = _db.CreateContext();
            rival.AppUsers.Add(new AppUser
            {
                Id = rivalId,
                FirebaseUid = "uid-race",
                Email = "rival@example.com",
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
            });
            await rival.SaveChangesAsync();
        });

        await using var context = _db.CreateContext(interceptor);
        var user = await new UserSync(context).GetOrCreateAsync(Caller("uid-race", "loser@example.com"));

        Assert.True(interceptor.Fired);
        Assert.Equal(rivalId, user.Id);
        Assert.Equal("loser@example.com", user.Email);

        await using var check = _db.CreateContext();
        Assert.Equal(1, await check.AppUsers.CountAsync(u => u.FirebaseUid == "uid-race"));
    }

    // Runs a callback just before the first SaveChanges, to play the part of a rival request.
    private sealed class RaceInterceptor(Func<Task> beforeFirstSave) : SaveChangesInterceptor
    {
        // True once the rival insert has run.
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await beforeFirstSave();
            }

            return result;
        }
    }
}
