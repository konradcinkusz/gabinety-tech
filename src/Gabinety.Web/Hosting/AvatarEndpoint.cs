using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Gabinety.Web.Api;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;

namespace Gabinety.Web.Hosting;

/// <summary>
/// <c>GET /avatar/{slug}</c> (ADR-0013): the picture of an approved practitioner is the avatar of the GitHub account they verified, fetched by this host so the browser never talks to GitHub and the CSP
/// (<c>img-src 'self' data:</c>) stays as it is. The only value of the request that is used is the slug, and only after it passed the slug rule; the login comes from the Directory profile and is validated
/// before it is put in the one URL this code ever builds (<c>https://github.com/&lt;login&gt;.png?size=96</c>). Redirects are followed here, by hand, and only to <c>avatars.githubusercontent.com</c>.
/// Anything else is a <c>404</c> with no body; neither the slug nor the login is logged (only an outcome counter).
/// </summary>
public static partial class AvatarEndpoint
{
    public const string Route = "/avatar/{slug}", ClientName = "github-avatar", AvatarHost = "avatars.githubusercontent.com";
    public const int MaxBytes = 200 * 1024, MaxRedirects = 3;
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(3), SuccessTtl = TimeSpan.FromHours(24), FailureTtl = TimeSpan.FromMinutes(10);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")] private static partial Regex SlugRule();
    [GeneratedRegex("^[A-Za-z0-9-]{1,39}$")] private static partial Regex LoginRule();

    static readonly Meter Meter = new("Gabinety.Web.Avatar");
    static readonly Counter<long> Outcomes = Meter.CreateCounter<long>("gabinety.web.avatar.requests", description: "Avatar requests by outcome (no slug, no login).");

    /// <summary>The one client for GitHub: no automatic redirects (they are followed by <see cref="FetchAsync"/>), and none of the backend headers.</summary>
    public static IServiceCollection AddAvatarClient(this IServiceCollection services)
    {
        services.AddSingleton<AvatarCache>();
        services.AddHttpClient(ClientName).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = FetchTimeout });
        return services;
    }

    public static WebApplication MapAvatarEndpoint(this WebApplication app)
    {
        app.MapGet(Route, ServeAsync).AllowAnonymous();
        return app;
    }

    static async Task<IResult> ServeAsync(string slug, HttpContext http, DirectoryClient directory, AvatarCache cache, IHttpClientFactory clients, TimeProvider time)
    {
        // A 404 of this route is an image that is missing, not a page: the status-code pages middleware must not put the not-found page in its body.
        if (http.Features.Get<IStatusCodePagesFeature>() is { } pages) pages.Enabled = false;
        if (slug.Length is < 3 or > 60 || !SlugRule().IsMatch(slug)) return Miss("invalid_slug");
        if (cache.TryGet(slug, time.GetUtcNow(), out var hit)) return hit is null ? Miss("cached_failure") : Serve(http, hit, "cached");

        var profile = await directory.ProfileAsync(slug, http.RequestAborted);
        var login = profile.IsOk ? profile.Value!.GithubLogin : null;
        if (login is null || !LoginRule().IsMatch(login)) return Fail(cache, slug, time, "no_login");

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        budget.CancelAfter(FetchTimeout);
        AvatarImage? image;
        try { image = await FetchAsync(clients.CreateClient(ClientName), login, budget.Token); }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException) { image = null; }
        if (image is null) return Fail(cache, slug, time, "failed");
        cache.Set(slug, time.GetUtcNow(), image);
        return Serve(http, image, "fetched");
    }

    /// <summary>The picture, or <c>null</c> for anything that is not exactly an image of the allowed type and size from the allowed host.</summary>
    public static async Task<AvatarImage?> FetchAsync(HttpClient client, string login, CancellationToken ct)
    {
        var uri = new Uri($"https://github.com/{login}.png?size=96");
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                if (response.Headers.Location is not { } location) return null;
                var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (next.Scheme != Uri.UriSchemeHttps || !next.IsDefaultPort || !string.Equals(next.Host, AvatarHost, StringComparison.OrdinalIgnoreCase)) return null;
                uri = next;
                continue;
            }
            if (response.StatusCode != System.Net.HttpStatusCode.OK) return null;
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is not ("image/png" or "image/jpeg")) return null;
            if (response.Content.Headers.ContentLength is > MaxBytes) return null;
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[MaxBytes + 1];
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await body.ReadAsync(buffer.AsMemory(total), ct);
                if (read == 0) break;
                total += read;
            }
            return total is 0 or > MaxBytes ? null : new AvatarImage(buffer[..total], type);
        }
        return null; // too many redirects
    }

    static IResult Serve(HttpContext http, AvatarImage image, string outcome)
    {
        Outcomes.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        http.Response.Headers.CacheControl = "public, max-age=86400";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.Bytes(image.Bytes, image.ContentType);
    }

    static IResult Fail(AvatarCache cache, string slug, TimeProvider time, string outcome)
    {
        cache.SetFailure(slug, time.GetUtcNow());
        return Miss(outcome);
    }

    static IResult Miss(string outcome)
    {
        Outcomes.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        return Results.StatusCode(StatusCodes.Status404NotFound);
    }
}

public sealed record AvatarImage(byte[] Bytes, string ContentType);

/// <summary>
/// The bytes of the pictures, by slug, in a cache of its own with a size limit (bytes; a failure counts as 1), so a stream of unknown slugs evicts old entries and cannot grow the process. Expiry is read
/// from <see cref="TimeProvider"/> by the caller (24 h for a picture, 10 min for a failure); the cache's own expiry only lets the memory go.
/// </summary>
public sealed class AvatarCache : IDisposable
{
    public const long SizeLimitBytes = 32L * 1024 * 1024;
    readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = SizeLimitBytes });
    sealed record Entry(AvatarImage? Image, DateTimeOffset Until);

    public bool TryGet(string slug, DateTimeOffset now, out AvatarImage? image)
    {
        image = null;
        if (!_cache.TryGetValue(slug, out Entry? entry) || entry is null || entry.Until <= now) return false;
        image = entry.Image;
        return true;
    }

    public void Set(string slug, DateTimeOffset now, AvatarImage image) => Put(slug, new Entry(image, now + AvatarEndpoint.SuccessTtl), image.Bytes.Length, AvatarEndpoint.SuccessTtl);
    public void SetFailure(string slug, DateTimeOffset now) => Put(slug, new Entry(null, now + AvatarEndpoint.FailureTtl), 1, AvatarEndpoint.FailureTtl);

    void Put(string slug, Entry entry, long size, TimeSpan ttl) => _cache.Set(slug, entry, new MemoryCacheEntryOptions { Size = size, AbsoluteExpirationRelativeToNow = ttl });

    public void Dispose() => _cache.Dispose();
}
