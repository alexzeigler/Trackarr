using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Trackarr.Services.Auth;

namespace Trackarr.Auth;

/// <summary>
///     An open page keeps the login it started with, because the cookie is only checked on HTTP requests. This checks
///     it again every minute, so a password change or a reset signs out pages that are already open too.
/// </summary>
internal sealed class RevalidatingAuthStateProvider(ILoggerFactory loggerFactory, UserService users)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        return users.IsCurrentAsync(authenticationState.User, cancellationToken);
    }
}
