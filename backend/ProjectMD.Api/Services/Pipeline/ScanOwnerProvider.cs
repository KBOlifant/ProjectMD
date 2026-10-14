using ProjectMD.Api.Auth;

namespace ProjectMD.Api.Services.Pipeline;

// Tells the pipeline whose scan this is, so AI spending can be charged to the right user.
// IScanGenerator.GenerateAsync only receives the URL, so the owner comes from the request instead.
public interface IScanOwnerProvider
{
    // The database id (AppUsers.Id) of the person making the current request.
    Task<Guid> GetUserIdAsync(CancellationToken ct);
}

// The real IScanOwnerProvider: resolves the signed-in caller through IUserSync (the same call the
// controller makes) and remembers the answer for the rest of the request, so the extra database
// round trip happens at most once.
public sealed class CurrentScanOwnerProvider : IScanOwnerProvider
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserSync _userSync;
    private Guid? _userId;

    // Receives the signed-in caller and the user sync.
    public CurrentScanOwnerProvider(ICurrentUser currentUser, IUserSync userSync)
    {
        _currentUser = currentUser;
        _userSync = userSync;
    }

    // Returns the caller's AppUsers id, looking it up on first use.
    public async Task<Guid> GetUserIdAsync(CancellationToken ct)
    {
        if (_userId is { } known)
        {
            return known;
        }

        var user = await _userSync.GetOrCreateAsync(_currentUser, ct);
        _userId = user.Id;
        return user.Id;
    }
}
