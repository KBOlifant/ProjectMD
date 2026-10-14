namespace ProjectMD.Api.Auth;

// The request-scoped ICurrentUser: reads the verified claims of the current HttpContext once and
// keeps the result for the rest of the request.
public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    private CurrentUser? _user;

    // Receives the accessor used to find the current request.
    public HttpCurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public string Uid => User.Uid;

    public string? Email => User.Email;

    public bool EmailVerified => User.EmailVerified;

    public string Role => User.Role;

    // The caller built from HttpContext.User. Asking for it on a request that is not
    // authenticated is a programming error (the endpoint is missing [Authorize]), so it throws
    // instead of returning an empty identity.
    private CurrentUser User =>
        _user ??= FirebaseClaims.Read(_accessor.HttpContext?.User)
            ?? throw new InvalidOperationException("There is no signed-in user on this request.");
}
