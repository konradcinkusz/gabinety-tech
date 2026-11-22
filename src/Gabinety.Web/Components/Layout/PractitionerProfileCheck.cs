using System.Collections.Concurrent;
using System.Security.Claims;
using Gabinety.Web.Api;
using Gabinety.Web.Auth;

namespace Gabinety.Web.Components.Layout;

/// <summary>
/// Does the signed-in account have a practitioner profile (D-2: only such an account sees <c>Panel programisty</c>)? No claim of the session carries it, so the answer is the Directory's
/// <c>GET /api/v1/me</c> (<see cref="MeDto.PractitionerId"/>). The header and the footer run on every page, so the answer is kept per SESSION for 60 seconds: a page costs at most one
/// Directory call per session per minute. The header and the footer render concurrently, so a per-session gate makes the second caller wait for the first and read its answer.
/// FAILS SOFT: when the Directory does not answer, the account is shown as not a practitioner (<c>Zostań programistą</c>, never the panel), and that answer is kept for 10 seconds
/// only, so the next page asks again. A profile created in the last minute appears after the cache expires. Process-wide, like <see cref="SessionCache"/>; the key is the session id,
/// which is random per sign-in and carries no personal data.
/// </summary>
public static class PractitionerProfileCheck
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60), FailureLifetime = TimeSpan.FromSeconds(10);
    static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(5);
    static readonly ConcurrentDictionary<string, Entry> Entries = new();
    static DateTimeOffset _lastSweep;

    sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public bool HasProfile;
        public DateTimeOffset Until;
    }

    public static async Task<bool> HasProfileAsync(ClaimsPrincipal? user, MeClient me, TimeProvider time, CancellationToken ct = default)
    {
        if (user?.Identity?.IsAuthenticated != true || user.FindFirst(SessionCookie.SessionClaim)?.Value is not { Length: > 0 } sid) return false;
        var entry = Entries.GetOrAdd(sid, _ => new Entry());
        await entry.Gate.WaitAsync(ct);
        try
        {
            var now = time.GetUtcNow();
            if (entry.Until > now) return entry.HasProfile;
            try
            {
                var result = await me.GetAsync(ct);
                entry.HasProfile = result.IsOk && result.Value!.PractitionerId is not null;
                entry.Until = now + (result.IsOk ? Lifetime : FailureLifetime);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                entry.HasProfile = false;
                entry.Until = now + FailureLifetime;
            }
            Sweep(now);
            return entry.HasProfile;
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    static void Sweep(DateTimeOffset now)
    {
        if (now - _lastSweep < SweepEvery) return;
        _lastSweep = now;
        foreach (var (sid, entry) in Entries)
            if (entry.Until <= now && entry.Gate.CurrentCount == 1) Entries.TryRemove(sid, out _);
    }
}
