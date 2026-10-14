using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectMD.Api.Auth;
using ProjectMD.Api.Dtos;
using ProjectMD.Api.Services;

namespace ProjectMD.Api.Controllers;

// Tells the frontend who the signed-in person is and how much of their monthly allowance is used.
// Any signed-in user may call it, whatever their role.
[ApiController]
[Authorize]
[Route("api/me")]
public class MeController : ControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserSync _userSync;
    private readonly IUsageService _usage;

    // Receives the verified caller, the user upsert helper and the usage calculator.
    public MeController(ICurrentUser currentUser, IUserSync userSync, IUsageService usage)
    {
        _currentUser = currentUser;
        _userSync = userSync;
        _usage = usage;
    }

    // GET /api/me creates or refreshes the caller's AppUser row (this is the first call after
    // sign-in) and returns { uid, email, emailVerified, role, usage }. Identity comes from the
    // verified token; the role is the one stored in the database.
    [HttpGet]
    public async Task<ActionResult<MeDto>> Get(CancellationToken cancellationToken)
    {
        var appUser = await _userSync.GetOrCreateAsync(_currentUser, cancellationToken);
        var usage = await _usage.GetAsync(appUser.Id, cancellationToken);

        return new MeDto(
            _currentUser.Uid,
            _currentUser.Email,
            _currentUser.EmailVerified,
            Roles.FromStored(appUser.Role),
            usage);
    }
}
