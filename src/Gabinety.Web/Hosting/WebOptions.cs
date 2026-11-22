using System.Net;
using Gabinety.Web.Api;

namespace Gabinety.Web.Hosting;

/// <summary>
/// Runtime configuration of the web host, read once from configuration (env vars use <c>__</c>), never baked into the image (P12, BLAZOR-WEB-APP section 14). Server side only: nothing
/// here reaches the browser. Names are those of the Next.js app (<c>web/src/lib/config</c>), so the AppHost and the deployment files set the same variables for either host.
/// </summary>
public sealed class WebOptions
{
    public static readonly string[] BackendNames = [Backend.Directory, Backend.Booking, Backend.Reputation, Backend.Assistant, Backend.Auth];

    /// <summary><c>Site:BaseUrl</c>: the public address. Decides the Secure flag of the cookies and is an allowed origin of the guard (a TLS-terminating proxy hides it from the request).</summary>
    public string SiteBaseUrl { get; init; } = "http://localhost:3001";

    /// <summary><c>RateLimit:TrustedProxyKey</c>, shared with the services: lets them trust the visitor address this host forwards (PR #110). Not the internal key; it opens no route.</summary>
    public string? TrustedProxyKey { get; init; }

    /// <summary><c>Identity:Issuer</c> and <c>Identity:Audience</c>: checked on the tokens of the identity service when set (not set in the fake profile, as in the Next.js app).</summary>
    public string? IdentityIssuer { get; init; }
    public string? IdentityAudience { get; init; }

    /// <summary><c>Identity:PublicUrl</c>: the address of the identity service as the BROWSER reaches it (the GitHub leg redirects the browser there); the first <c>Backend:Auth</c> candidate when unset (the fake profile, where both are localhost).</summary>
    public string? IdentityPublicUrl { get; init; }

    /// <summary><c>Backend:&lt;Name&gt;</c>: an ordered, comma separated candidate list of base addresses per backend. They become service-discovery endpoints (see <see cref="ServiceConfiguration"/>).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Backends { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    public bool SecureCookies => SiteBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public static WebOptions From(IConfiguration config)
    {
        static string? Opt(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        var backends = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var name in BackendNames)
        {
            backends[name] = (config[$"Backend:{char.ToUpperInvariant(name[0])}{name[1..]}"] ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.TrimEnd('/')).Where(c => Uri.TryCreate(c, UriKind.Absolute, out var u) && u.Scheme is "http" or "https").ToList();
        }
        return new WebOptions
        {
            SiteBaseUrl = (Opt(config["Site:BaseUrl"]) ?? "http://localhost:3001").TrimEnd('/'),
            TrustedProxyKey = Opt(config["RateLimit:TrustedProxyKey"]),
            IdentityIssuer = Opt(config["Identity:Issuer"]),
            IdentityAudience = Opt(config["Identity:Audience"]),
            IdentityPublicUrl = Opt(config["Identity:PublicUrl"])?.TrimEnd('/'),
            Backends = backends,
        };
    }

    /// <summary>
    /// The candidates of each backend as the configuration the service-discovery provider reads (<c>Services:&lt;name&gt;:&lt;scheme&gt;:&lt;i&gt;</c>), so a typed client's base address is the logical
    /// name (<c>https+http://directory</c>) on a laptop, under the AppHost and on a platform (BLAZOR-WEB-APP section 5). The Next.js "candidate ladder" that probes each candidate is replaced
    /// by the platform's discovery: here the candidates are endpoints of one service.
    /// </summary>
    public IEnumerable<KeyValuePair<string, string?>> ServiceConfiguration()
    {
        foreach (var (name, candidates) in Backends)
            for (var i = 0; i < candidates.Count; i++)
                yield return new($"Services:{name}:{new Uri(candidates[i]).Scheme}:{i}", candidates[i]);
    }

    /// <summary>One IP address, nothing else (the shape the services accept in X-Client-Address).</summary>
    public static bool IsAddress(string? value) => value is { Length: > 0 and <= 45 } && IPAddress.TryParse(value, out _) && !value.Contains(',');
}
