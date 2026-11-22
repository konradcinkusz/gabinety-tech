using System.Collections.Concurrent;

namespace Gabinety.Web.Auth;

/// <summary>What the cookie's principal is built from when it is re-issued after a rotation: the verified claims of the newest access token.</summary>
public sealed record SessionIdentity(string Subject, string? Email, IReadOnlyList<string> Roles);

/// <summary>
/// The per-session holder of the NEWEST tokens (a process-wide cache, not the session: the session is the sealed cookie, ADR-0010 cutover precondition "deploys must not sign users out").
/// Why it exists: the refresh token of the identity service rotates on every use and a replay revokes the whole family, while only a request can write a cookie (a circuit has no
/// response). So whoever rotates (a request or a circuit) leaves the newest refresh token here and the next response carries it into the cookie (<see cref="SessionCookie.CatchUpAsync"/>).
/// A restart empties the cache and loses nothing but the access token: the cookie's refresh token is then the seed. Entries are small (about 1.5 KB) and swept after the cookie's lifetime.
/// One entry per session id, with the lock that serialises the rotation of that session (the keyed lock: two simultaneous callers never spend one refresh token twice).
/// </summary>
public sealed class SessionCache(TimeProvider time)
{
    public static readonly TimeSpan Retention = SessionCookie.Lifetime + TimeSpan.FromHours(1);
    static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(5);

    public sealed class Entry
    {
        internal Entry(string refreshToken, DateTimeOffset now) { RefreshToken = refreshToken; Touched = now; }
        public readonly SemaphoreSlim Gate = new(1, 1);
        public string? AccessToken { get; internal set; }
        public DateTimeOffset AccessExpiresAt { get; internal set; }
        /// <summary>The newest refresh token this process knows of for the session (never older than the cookie's, except after a restart, when the cookie's is the seed).</summary>
        public string RefreshToken { get; internal set; }
        public SessionIdentity? Identity { get; internal set; }
        /// <summary>The identity service said the session is over (a refused refresh, a logout): the cookie is rejected and a circuit ends at its next revalidation.</summary>
        public bool Dead { get; internal set; }
        /// <summary>After a 429, a 5xx or an outage the identity service is not asked again before this moment (the session stays signed in).</summary>
        public DateTimeOffset RetryNotBefore { get; internal set; }
        internal DateTimeOffset Touched { get; set; }
    }

    readonly ConcurrentDictionary<string, Entry> _entries = new();
    DateTimeOffset _lastSweep;

    public int Count => _entries.Count;

    public Entry? Find(string sid) => _entries.TryGetValue(sid, out var e) ? Touch(e) : null;

    /// <summary>The entry of a session, created from the cookie's refresh token when this process has not seen the session yet (after a restart).</summary>
    public Entry GetOrSeed(string sid, string refreshToken)
    {
        Sweep();
        return Touch(_entries.GetOrAdd(sid, _ => new Entry(refreshToken, time.GetUtcNow())));
    }

    /// <summary>A sign-in: the first tokens of a new session.</summary>
    public void Open(string sid, string accessToken, DateTimeOffset accessExpiresAt, string refreshToken, SessionIdentity identity)
    {
        Sweep();
        _entries[sid] = new Entry(refreshToken, time.GetUtcNow()) { AccessToken = accessToken, AccessExpiresAt = accessExpiresAt, Identity = identity };
    }

    /// <summary>Marks the session over. The entry stays (until the sweep) so that the same cookie is refused here without asking the identity service again.</summary>
    public void End(string sid, string refreshToken)
    {
        var entry = _entries.GetOrAdd(sid, _ => new Entry(refreshToken, time.GetUtcNow()));
        entry.Dead = true;
        entry.AccessToken = null;
        entry.Touched = time.GetUtcNow();
    }

    public bool IsAlive(string sid) => Find(sid) is not { Dead: true };

    Entry Touch(Entry e) { e.Touched = time.GetUtcNow(); return e; }

    void Sweep()
    {
        var now = time.GetUtcNow();
        if (now - _lastSweep < SweepEvery) return;
        _lastSweep = now;
        foreach (var (sid, e) in _entries)
            if (now - e.Touched > Retention && e.Gate.CurrentCount == 1) _entries.TryRemove(sid, out _);
    }
}
