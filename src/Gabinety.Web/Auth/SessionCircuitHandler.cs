using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Gabinety.Web.Auth;

/// <summary>Captures the session cookie's refresh token when a circuit opens (the only moment a circuit has the request): the seed of the session when this process has not seen it yet.</summary>
public sealed class SessionCircuitHandler(ISessionSeed seed) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken) => seed.CaptureAsync();
}
