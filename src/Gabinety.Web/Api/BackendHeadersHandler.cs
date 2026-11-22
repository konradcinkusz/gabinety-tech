using System.Net.Http.Headers;
using Gabinety.Web.Hosting;

namespace Gabinety.Web.Api;

/// <summary>
/// The one handler that decides which headers leave this host for a backend (PR #110 and ADR-0007): the user's bearer when the wrapper put one on the request, and the visitor's address with
/// the shared proxy key (<c>X-Client-Address</c> + <c>X-Edge-Key</c>) so the per-visitor rate limits of CONTRACTS 10.2 key on the visitor and not on this machine. Both are set here from
/// the request's options and never copied from an incoming request. The key is sent only with an address, and only when it is configured. It is NOT the internal key and opens no route.
/// </summary>
public sealed class BackendHeadersHandler(WebOptions options) : DelegatingHandler
{
    public const string ClientAddressHeader = "X-Client-Address", EdgeKeyHeader = "X-Edge-Key";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove(ClientAddressHeader);
        request.Headers.Remove(EdgeKeyHeader);
        if (request.Options.TryGetValue(RequestContext.AccessToken, out var token) && !string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (options.TrustedProxyKey is { } key && request.Options.TryGetValue(RequestContext.VisitorAddress, out var address) && WebOptions.IsAddress(address))
        {
            request.Headers.TryAddWithoutValidation(ClientAddressHeader, address);
            request.Headers.TryAddWithoutValidation(EdgeKeyHeader, key);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
