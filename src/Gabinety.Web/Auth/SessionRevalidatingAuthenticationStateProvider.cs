using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.Options;

namespace Gabinety.Web.Auth;

public sealed class CircuitRevalidationOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// A circuit does not re-authenticate on every call: its websocket outlives the cookie (BLAZOR-WEB-APP section 7, trap 3). This provider asks the session cache every
/// <see cref="CircuitRevalidationOptions.Interval"/> whether the session the circuit started under is still alive; when it is not (the cookie's lifetime, a sign-out, a refused refresh) the authentication state turns anonymous and the signed-in markup of the page disappears, and the page's own calls no longer find a token. A circuit of an anonymous visitor
/// has no session and is not affected.
/// </summary>
public class SessionRevalidatingAuthenticationStateProvider(ILoggerFactory loggers, SessionCache cache, TimeProvider time, IOptions<CircuitRevalidationOptions> options)
    : RevalidatingServerAuthenticationStateProvider(loggers)
{
    protected override TimeSpan RevalidationInterval => options.Value.Interval;

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        var user = authenticationState.User;
        if (user.Identity?.IsAuthenticated != true) return Task.FromResult(true);
        var expires = long.TryParse(user.FindFirst(SessionCookie.ExpiryClaim)?.Value, out var at) ? DateTimeOffset.FromUnixTimeSeconds(at) : (DateTimeOffset?)null;
        return Task.FromResult(user.FindFirst(SessionCookie.SessionClaim)?.Value is { } sid && cache.IsAlive(sid) && expires is { } e && e > time.GetUtcNow());
    }
}
