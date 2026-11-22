using System.Net;
using Gabinety.Web.Hosting;

namespace Gabinety.Web.Api;

/// <summary>
/// What a client wrapper hands to the handler of its request through <see cref="HttpRequestMessage.Options"/>. Message handlers live in their own DI scope, not the circuit's
/// (BLAZOR-WEB-APP section 7, trap 2), so a scoped service injected into a handler would NOT be the circuit's instance: the wrapper, which does run in the circuit's scope, reads the
/// token and the visitor address and puts them on the request.
/// </summary>
public static class RequestContext
{
    public static readonly HttpRequestOptionsKey<string> AccessToken = new("gabinety.access-token");
    public static readonly HttpRequestOptionsKey<string> VisitorAddress = new("gabinety.visitor-address");
    /// <summary>The attempt budget of ONE request, for the calls SPEC 5.1 gives more than the 5 s of a read (the payment provider call of a booking: 10 s; the assistant: 35 s). Read by <see cref="ApiResilience"/>.</summary>
    public static readonly HttpRequestOptionsKey<TimeSpan> Timeout = new("gabinety.timeout");
}

/// <summary>
/// The visitor's address as the platform in front of this host reports it (CONTRACTS 10.2: the services limit per client address; every visitor reaches them from this machine).
/// Same rule as <c>web/src/lib/api/clientAddress.ts</c>: the platform's own header wins (<c>Fly-Client-IP</c>, set by the edge, not by the visitor); otherwise the LAST entry of
/// <c>X-Forwarded-For</c>, which the nearest proxy appended, never the first, which the visitor can write; otherwise (a difference from the Next.js app, which then sends nothing)
/// the address of the connection, which for a host reached directly is the visitor. Anything that is not exactly one IP address is ignored.
/// </summary>
public static class ClientAddress
{
    public static string? Of(HttpContext? context)
    {
        if (context is null) return null;
        var h = context.Request.Headers;
        var platform = Valid(h["Fly-Client-IP"].ToString());
        if (platform is not null) return platform;
        var forwarded = h["X-Forwarded-For"].ToString();
        if (forwarded.Length > 0) return Valid(forwarded.Split(',')[^1]);
        return context.Connection.RemoteIpAddress is { } ip ? (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString() : null;
    }

    static string? Valid(string raw)
    {
        var v = raw.Trim();
        return WebOptions.IsAddress(v) ? (IPAddress.Parse(v) is { IsIPv4MappedToIPv6: true } m ? m.MapToIPv4().ToString() : v) : null;
    }
}

/// <summary>
/// The visitor address for the backend calls of one request or one circuit. In a request it is read from the request; a circuit has no <c>HttpContext</c> once it runs, so the circuit
/// handler captures the address when the circuit opens (<see cref="Capture"/>).
/// </summary>
public sealed class VisitorAddressAccessor(IHttpContextAccessor accessor)
{
    string? _captured;
    public string? Current => ClientAddress.Of(accessor.HttpContext) ?? _captured;
    public void Capture() => _captured = ClientAddress.Of(accessor.HttpContext) ?? _captured;
}
