using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Gabinety.Web.Auth;

/// <summary>
/// The server side of a sign-in (BLAZOR-WEB-APP section 7): the cookie holds only an opaque session id (HttpOnly, never readable by script), the identity service's tokens and the ticket
/// stay here. Because the tokens are in server memory, a circuit can refresh them without a response to write a cookie into (a circuit has none), and the browser never holds a token.
/// It is also what ends a circuit at the end of the session (<see cref="SessionRevalidatingAuthenticationStateProvider"/>). A restart or a second instance loses the sessions: the hosting
/// rules of ADR-0010 (one instance, same as the circuits) say so. The ticket and the tokens of one session are one entry, removed together.
/// </summary>
public sealed class ServerSessionStore(TimeProvider time) : ITicketStore
{
    public const string SessionClaim = "sid";
    static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(1);

    sealed class Entry(SessionTokens tokens, DateTimeOffset created)
    {
        public AuthenticationTicket? Ticket;
        public SessionTokens Tokens = tokens;
        public readonly DateTimeOffset Created = created;
        public readonly SemaphoreSlim RefreshGate = new(1, 1); // the refresh token is single use: one refresh per session at a time
    }

    readonly ConcurrentDictionary<string, Entry> _entries = new();

    public int Count => _entries.Count;

    /// <summary>Registers the tokens of a sign-in and returns the session id, which goes into the principal as the <see cref="SessionClaim"/> claim before <c>SignInAsync</c>.</summary>
    public string Open(SessionTokens tokens)
    {
        Sweep();
        var sid = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        _entries[sid] = new Entry(tokens, time.GetUtcNow());
        return sid;
    }

    public SessionTokens? TokensOf(string sid) => Live(sid)?.Tokens;

    public bool IsAlive(string sid) => Live(sid) is not null;

    public void Update(string sid, SessionTokens tokens) { if (_entries.TryGetValue(sid, out var e)) e.Tokens = tokens; }

    public SemaphoreSlim? RefreshGate(string sid) => Live(sid)?.RefreshGate;

    public void Close(string sid) => _entries.TryRemove(sid, out _);

    // ---- ITicketStore (called by the cookie handler) ----

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var sid = ticket.Principal.FindFirst(SessionClaim)?.Value;
        if (sid is null || !_entries.TryGetValue(sid, out var e)) throw new InvalidOperationException("A sign-in must open its session (ServerSessionStore.Open) before the cookie is issued.");
        e.Ticket = ticket;
        return Task.FromResult(sid);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        if (_entries.TryGetValue(key, out var e)) e.Ticket = ticket;
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => Task.FromResult(Live(key)?.Ticket);

    public Task RemoveAsync(string key) { Close(key); return Task.CompletedTask; }

    // A session is live while its ticket exists and has not expired: the same moment the cookie expires, so a circuit cannot outlive the sign-in it started from.
    Entry? Live(string sid)
    {
        if (!_entries.TryGetValue(sid, out var e)) return null;
        var now = time.GetUtcNow();
        var expired = e.Ticket is { } t ? t.Properties.ExpiresUtc is { } at && at <= now : now - e.Created > PendingLifetime;
        if (expired) { _entries.TryRemove(sid, out _); return null; }
        return e;
    }

    void Sweep()
    {
        foreach (var sid in _entries.Keys) _ = Live(sid);
    }
}
