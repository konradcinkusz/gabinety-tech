using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace Gabinety.ServiceDefaults;

/// <summary>One sliding window of a limit: at most <see cref="Permits"/> requests in any <see cref="Window"/>.</summary>
public readonly record struct RateLimitRule(TimeSpan Window, int Permits);

/// <summary>
/// The table of CONTRACTS 10.2 as code: which endpoint group a request belongs to and the windows that group is held to. Per instance, in memory; the key is the user id
/// when signed in, else the client address (the caller builds the partition key; see <see cref="PartitionKey"/>).
/// <list type="table">
/// <item><term>reads</term><description>public <c>GET</c>s: practitioners (list, profile, slots, reviews), taxonomy, sitemap, ranking rules. 120 per minute</description></item>
/// <item><term>bookings</term><description><c>POST /api/v1/bookings</c>. 10 per minute and 30 per day</description></item>
/// <item><term>reviews</term><description><c>POST /api/v1/reviews</c>. 5 per hour</description></item>
/// <item><term>webhooks</term><description>none by address (the signature decides); the 64 KB body limit is the endpoint's own</description></item>
/// <item><term>other</term><description>everything else, <c>perMinute</c> per minute (default 60)</description></item>
/// </list>
/// <c>POST /api/v1/assistant/brief</c> keeps its own per-user limiter in the Assistant (5 per minute and 20 per day, <c>BriefRateLimiter</c>); here it is "everything else".
/// A configured <c>RateLimit:PerMinute</c> above 60 (the AppHost fake profile only) scales every row by that ratio so one shared test address does not trip them;
/// at or below 60 only "everything else" follows it.
/// </summary>
public static class RateLimitTable
{
    public const string Reads = "reads", Bookings = "bookings", Reviews = "reviews", Webhooks = "webhooks", Other = "other";
    static readonly string[] PublicReadRoots = ["/api/v1/practitioners", "/api/v1/taxonomy", "/api/v1/sitemap", "/api/v1/ranking-rules"];

    /// <summary>The group of a request, or <see cref="Webhooks"/> for the unlimited one.</summary>
    public static string GroupOf(string method, PathString path)
    {
        var p = path.Value ?? "";
        if (p.StartsWith("/webhooks/", StringComparison.OrdinalIgnoreCase)) return Webhooks;
        if (HttpMethods.IsPost(method))
        {
            var exact = p.TrimEnd('/');
            if (exact.Equals("/api/v1/bookings", StringComparison.OrdinalIgnoreCase)) return Bookings;
            if (exact.Equals("/api/v1/reviews", StringComparison.OrdinalIgnoreCase)) return Reviews;
        }
        else if (HttpMethods.IsGet(method) && PublicReadRoots.Any(root => IsUnder(p, root))) return Reads;
        return Other;
    }

    static bool IsUnder(string path, string root) =>
        path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && (path.Length == root.Length || path[root.Length] == '/');

    public static RateLimitRule[] RulesOf(string group, int perMinute)
    {
        var scale = Math.Max(1, perMinute / (double)DefaultPerMinute);
        int Scaled(int n) => (int)Math.Ceiling(n * scale);
        return group switch
        {
            Reads => [new(TimeSpan.FromMinutes(1), Scaled(120))],
            Bookings => [new(TimeSpan.FromMinutes(1), Scaled(10)), new(TimeSpan.FromDays(1), Scaled(30))],
            Reviews => [new(TimeSpan.FromHours(1), Scaled(5))],
            _ => [new(TimeSpan.FromMinutes(1), perMinute)],
        };
    }

    public const int DefaultPerMinute = 60;

    public const string ClientAddressHeader = "X-Client-Address", TrustedProxyKeyHeader = "X-Edge-Key";

    /// <summary>
    /// The user id (<c>sub</c>, or the name identifier JwtBearer may map it to) when signed in, else the address, else "unknown". Prefixed so a user id can never equal an address.
    /// Behind the web server every anonymous visitor arrives from the web machine, so the web app forwards the visitor's address in <see cref="ClientAddressHeader"/> together with
    /// <see cref="TrustedProxyKeyHeader"/>; the address is used ONLY when <paramref name="trustedProxyKey"/> is configured and the header equals it (constant time) and the value is
    /// exactly one IP address. Anything else, from anyone, is ignored. That key is NOT the internal key: it opens no route.
    /// </summary>
    public static string PartitionKey(string group, HttpContext ctx, string? trustedProxyKey = null)
    {
        var user = (ctx.User.FindFirst("sub") ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier))?.Value;
        var who = !string.IsNullOrEmpty(user) ? "u:" + user : "a:" + (AddressOf(ctx, trustedProxyKey) ?? "unknown");
        return group + "|" + who;
    }

    static string? AddressOf(HttpContext ctx, string? trustedProxyKey)
    {
        if (!string.IsNullOrEmpty(trustedProxyKey))
        {
            var given = ctx.Request.Headers[TrustedProxyKeyHeader].ToString();
            var forwarded = ctx.Request.Headers[ClientAddressHeader].ToString();
            if (given.Length > 0 && forwarded.Length > 0 &&
                CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(trustedProxyKey), Encoding.UTF8.GetBytes(given)) &&
                IPAddress.TryParse(forwarded, out var ip) && !forwarded.Contains(','))
                return Normalise(ip);
        }
        return ctx.Connection.RemoteIpAddress is { } remote ? Normalise(remote) : null;
    }

    static string Normalise(IPAddress ip) => (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
}

/// <summary>
/// A sliding-window limiter driven by <see cref="TimeProvider"/> (so a test moves a fake clock instead of sleeping) that holds one key to several windows at once.
/// Exact, not approximated: every admitted request is remembered until the longest window has passed. A refused request is not remembered, so being refused never
/// extends the wait. Thread safe.
/// </summary>
public sealed class WindowedRateLimiter(TimeProvider time, params RateLimitRule[] rules) : RateLimiter
{
    readonly object _gate = new();
    readonly Queue<long> _admitted = new(); // UtcTicks, oldest first
    readonly TimeSpan _longest = rules.Max(r => r.Window);
    long _last;

    public override TimeSpan? IdleDuration
    {
        get
        {
            lock (_gate)
            {
                var now = time.GetUtcNow().UtcTicks;
                Prune(now);
                // Never idle while a window still remembers a request: evicting this limiter would forget the count.
                return _admitted.Count > 0 ? null : TimeSpan.FromTicks(Math.Max(0, now - _last));
            }
        }
    }

    public override RateLimiterStatistics? GetStatistics() => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        lock (_gate)
        {
            var now = time.GetUtcNow().UtcTicks;
            Prune(now);
            var wait = TimeSpan.Zero;
            foreach (var rule in rules)
            {
                var inside = _admitted.Where(t => now - t < rule.Window.Ticks).Order().ToList();
                if (inside.Count + 1 <= rule.Permits) continue;
                // The request passes when enough of the oldest ones have left this window.
                var free = inside[inside.Count - rule.Permits];
                wait = TimeSpan.FromTicks(Math.Max(wait.Ticks, free + rule.Window.Ticks - now));
            }
            if (wait > TimeSpan.Zero) return new Lease(false, wait);
            _admitted.Enqueue(now);
            _last = now;
            return new Lease(true, null);
        }
    }

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) => new(AttemptAcquireCore(permitCount));

    void Prune(long now) { while (_admitted.Count > 0 && now - _admitted.Peek() >= _longest.Ticks) _admitted.Dequeue(); }

    sealed class Lease(bool acquired, TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => acquired;
        public override IEnumerable<string> MetadataNames => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (retryAfter is { } r && metadataName == MetadataName.RetryAfter.Name) { metadata = r; return true; }
            metadata = null;
            return false;
        }
    }
}
