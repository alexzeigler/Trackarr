using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Trackarr.Services.Auth;

namespace Trackarr.Auth;

/// <summary>
///     An open page keeps the login it started with, because the cookie is only checked on HTTP requests. This checks
///     it again every minute, so a password change or a reset signs out pages that are already open too. A page opened
///     through local network access closes the same way once that option is turned off or its network is removed.
/// </summary>
internal sealed class RevalidatingAuthStateProvider(
    ILoggerFactory loggerFactory,
    UserService users,
    LocalAccess localAccess)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        var user = authenticationState.User;
        return LocalAccess.IsLocalPrincipal(user)
            ? Task.FromResult(localAccess.StillAllows(user))
            : users.IsCurrentAsync(user, cancellationToken);
    }
}
