using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace Pavlov.Manager.Services;

/// <summary>Signs open circuits out when the RCON session they signed in to is gone.</summary>
public sealed class RconAuthenticationStateProvider(ILoggerFactory loggerFactory, RconSession session)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken) =>
        Task.FromResult(session.IsValid(authenticationState.User));

    public static ClaimsPrincipal CreatePrincipal(string generation) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "admin"), new Claim(RconSession.GenerationClaim, generation)],
            "rcon"));
}
