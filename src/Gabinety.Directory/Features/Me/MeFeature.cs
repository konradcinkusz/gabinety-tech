using System.Security.Claims;
using Gabinety.ServiceDefaults;

namespace Gabinety.Directory.Features.Me;

/// <summary>GET /api/v1/me behind JWT validation against authservice's JWKS (CONTRACTS 3.3). The practitioner and moderator facts arrive with T-03.x.</summary>
public sealed class MeFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) { } // JWT authentication is registered once, in Program.cs

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/me", (ClaimsPrincipal user) =>
        {
            // "sub" stays "sub" or is mapped to NameIdentifier, depending on the handler's inbound mapping.
            var userId = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return userId is null
                ? Results.Unauthorized()
                : Results.Ok(new Contracts.Directory.MeDto(userId, null, null,
                    user.IsInRole("Admin") || user.IsInRole("SuperAdmin"), false));
        }).RequireAuthorization();
}
