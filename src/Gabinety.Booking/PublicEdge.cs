using Microsoft.AspNetCore.Http;

namespace Gabinety.Booking;

/// <summary>
/// What Booking serves to a request that arrived through the public Fly edge (F-03, SPEC 4.3). Fly cannot expose one path, so the app filters.
/// Served: the payment webhook, the liveness probe, and the client API (<c>/api/v1/*</c>: the public slot reads and the token-protected calls).
/// Not served: <c>/internal/*</c> (also guarded by the shared middleware), <c>/api/v1/admin/*</c> (the web app reaches it over the private network), health details and OpenAPI.
/// </summary>
public static class PublicEdge
{
    public static bool Serves(HttpRequest r)
    {
        var p = r.Path;
        if (p.StartsWithSegments("/webhooks/payments", StringComparison.OrdinalIgnoreCase)) return HttpMethods.IsPost(r.Method);
        if (p.StartsWithSegments("/alive", StringComparison.OrdinalIgnoreCase)) return true;
        if (p.StartsWithSegments("/api/v1/admin", StringComparison.OrdinalIgnoreCase)) return false;
        return p.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase);
    }
}
